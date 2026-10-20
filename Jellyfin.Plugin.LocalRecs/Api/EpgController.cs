using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Epg;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Einrichten und Testen des eigenen Fernsehprogramm-Anbieters „LocalRecs EPG“ (nur Admins).
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Epg")]
    [Authorize(Policy = "RequiresElevation")]
    public class EpgController : ControllerBase
    {
        private readonly EpgListingsProvider _provider;
        private readonly IListingsManager _listings;
        private readonly IConfigurationManager _configuration;
        private readonly ITaskManager _tasks;

        /// <summary>
        /// Initializes a new instance of the <see cref="EpgController"/> class.
        /// </summary>
        /// <param name="provider">The EPG provider.</param>
        /// <param name="listings">Jellyfin's listings manager.</param>
        /// <param name="configuration">Configuration manager (live TV settings).</param>
        /// <param name="tasks">Task manager (guide refresh).</param>
        public EpgController(EpgListingsProvider provider, IListingsManager listings, IConfigurationManager configuration, ITaskManager tasks)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _listings = listings ?? throw new ArgumentNullException(nameof(listings));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        private LiveTvOptions LiveTv => (LiveTvOptions)_configuration.GetConfiguration("livetv");

        /// <summary>
        /// Gets all programme guide providers of Jellyfin (to take over the channel mapping).
        /// </summary>
        /// <returns>The providers.</returns>
        [HttpGet("Providers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<ProviderSummary>> GetProviders()
        {
            return Ok(LiveTv.ListingProviders.Select((p, i) => new ProviderSummary
            {
                Id = p.Id,
                Type = p.Type,
                Path = p.Path,
                Mappings = p.ChannelMappings?.Length ?? 0,
                Position = i + 1,
                IsLocalRecs = string.Equals(p.Type, EpgListingsProvider.ProviderType, StringComparison.OrdinalIgnoreCase)
            }));
        }

        /// <summary>
        /// Creates or updates the LocalRecs EPG provider. Path, tuners and channel mapping can be taken over from an existing
        /// provider (e.g. the built-in XMLTV one). The new provider is put first, so Jellyfin uses it before the old one.
        /// </summary>
        /// <param name="request">Source and provider to take over from.</param>
        /// <returns>The provider.</returns>
        [HttpPost("Setup")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ProviderSummary>> Setup([FromBody] SetupRequest request)
        {
            var options = LiveTv;
            var from = string.IsNullOrEmpty(request?.FromProviderId) ? null
                : options.ListingProviders.FirstOrDefault(p => string.Equals(p.Id, request.FromProviderId, StringComparison.OrdinalIgnoreCase));
            var existing = options.ListingProviders.FirstOrDefault(p => string.Equals(p.Type, EpgListingsProvider.ProviderType, StringComparison.OrdinalIgnoreCase));
            var path = string.IsNullOrWhiteSpace(request?.Path) ? from?.Path ?? existing?.Path : request.Path.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest("Bitte die XMLTV-Quelle angeben oder einen Anbieter zum Übernehmen wählen.");
            }

            var info = existing ?? new ListingsProviderInfo();
            info.Type = EpgListingsProvider.ProviderType;
            info.Path = path;
            if (from != null)
            {
                info.EnableAllTuners = from.EnableAllTuners;
                info.EnabledTuners = from.EnabledTuners ?? Array.Empty<string>();
                info.ChannelMappings = from.ChannelMappings ?? Array.Empty<MediaBrowser.Model.Dto.NameValuePair>();
                info.PreferredLanguage = from.PreferredLanguage;
                info.UserAgent = from.UserAgent;
            }
            else if (existing == null)
            {
                info.EnableAllTuners = true;
            }

            // Kategorienlisten von Jellyfin bleiben leer – die Regel-Bausteine entscheiden
            info.MovieCategories = Array.Empty<string>();
            info.KidsCategories = Array.Empty<string>();
            info.NewsCategories = Array.Empty<string>();
            info.SportsCategories = Array.Empty<string>();

            try
            {
                info = await _listings.SaveListingProvider(info, false, false).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is InvalidOperationException)
            {
                return BadRequest(ex.Message);
            }

            // An die erste Stelle, damit Jellyfin diesen Anbieter vor dem alten nutzt
            options = LiveTv;
            var saved = options.ListingProviders.First(p => string.Equals(p.Id, info.Id, StringComparison.OrdinalIgnoreCase));
            options.ListingProviders = new[] { saved }.Concat(options.ListingProviders.Where(p => !ReferenceEquals(p, saved))).ToArray();
            _configuration.SaveConfiguration("livetv", options);
            QueueGuideRefresh();
            return Ok(new ProviderSummary { Id = info.Id, Type = info.Type, Path = info.Path, Mappings = info.ChannelMappings?.Length ?? 0, Position = 1, IsLocalRecs = true });
        }

        /// <summary>
        /// Loads the source again and refreshes the guide (e.g. after changing the rules).
        /// </summary>
        /// <returns>No content.</returns>
        [HttpPost("Refresh")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Refresh()
        {
            _provider.ClearCache();
            QueueGuideRefresh();
            return NoContent();
        }

        /// <summary>
        /// Tests the rules on the current source: counts per kind and example programmes with the reasons.
        /// </summary>
        /// <param name="search">Only programmes whose title contains this text, optional.</param>
        /// <param name="kind">Only "series", "movie", "other", "kids", "news" or "sports", optional.</param>
        /// <param name="limit">Number of examples (default 50).</param>
        /// <param name="path">Source to test instead of the configured one, optional.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The test result.</returns>
        [HttpGet("Test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TestResult>> Test([FromQuery] string? search, [FromQuery] string? kind, [FromQuery] int limit = 50, [FromQuery] string? path = null, CancellationToken cancellationToken = default)
        {
            var info = string.IsNullOrWhiteSpace(path)
                ? LiveTv.ListingProviders.FirstOrDefault(p => string.Equals(p.Type, EpgListingsProvider.ProviderType, StringComparison.OrdinalIgnoreCase))
                    ?? LiveTv.ListingProviders.FirstOrDefault()
                : new ListingsProviderInfo { Id = "test", Path = path.Trim() };
            if (info == null || string.IsNullOrWhiteSpace(info.Path))
            {
                return NotFound("Kein Fernsehprogramm-Anbieter eingerichtet");
            }

            EpgFile file;
            try
            {
                file = await _provider.GetFileAsync(new ListingsProviderInfo { Id = (info.Id ?? "test") + "-test", Path = info.Path, PreferredLanguage = info.PreferredLanguage, UserAgent = info.UserAgent }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.IO.IOException || ex is InvalidOperationException || ex is System.Xml.XmlException || ex is TaskCanceledException)
            {
                return BadRequest("XMLTV-Quelle nicht lesbar: " + ex.Message);
            }

            var settings = EpgRuleSettings.From(Config);
            await _provider.WaitForExtraSourceAsync(cancellationToken).ConfigureAwait(false);
            var names = file.Channels.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
            var result = new TestResult
            {
                Source = info.Path,
                Channels = file.Channels.Count,
                Rules = new Dictionary<string, string>
                {
                    ["Nie Film"] = string.Join(", ", settings.NoMovieCategories),
                    ["Weder Film noch Serie (Kategorien)"] = string.Join(", ", settings.OtherCategories),
                    ["Weder Film noch Serie (Sender)"] = string.Join(", ", settings.OtherChannels),
                    ["Film-Kategorien"] = string.Join(", ", settings.MovieCategories),
                    ["Mindestlänge Film"] = settings.MovieMinMinutes.ToString(CultureInfo.InvariantCulture),
                    ["Merkmale"] = string.Join(", ", new[] { settings.MovieByDirector ? "Regie" : null, settings.MovieByImdb ? "IMDb" : null, settings.MovieByYear ? "Jahr allein" : null }.Where(x => x != null))
                }
            };
            var examples = new List<TestProgramme>();
            foreach (var programme in file.Programmes)
            {
                names.TryGetValue(programme.ChannelId, out var channelName);
                var extra = _provider.Extra(programme, channelName);
                var c = EpgClassifier.Classify(programme, channelName, settings, extra);
                result.Enriched += extra != null ? 1 : 0;
                result.Programmes++;
                result.Series += c.IsSeries ? 1 : 0;
                result.Movies += c.IsMovie ? 1 : 0;
                result.Other += !c.IsSeries && !c.IsMovie ? 1 : 0;
                result.Kids += c.IsKids ? 1 : 0;
                result.News += c.IsNews ? 1 : 0;
                result.Sports += c.IsSports ? 1 : 0;
                result.SeriesWithEpisodeNumber += c.IsSeries && (c.Season != null || c.Episode != null) ? 1 : 0;
                result.LongWithoutMovieEvidence += !c.IsSeries && !c.IsMovie && programme.Minutes >= settings.MovieMinMinutes ? 1 : 0;
                if (examples.Count >= Math.Clamp(limit, 1, 500)
                    || (!string.IsNullOrWhiteSpace(search) && !programme.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                    || !MatchesKind(c, kind))
                {
                    continue;
                }

                examples.Add(new TestProgramme
                {
                    Title = programme.Title,
                    SubTitle = programme.SubTitle,
                    Channel = channelName ?? programme.ChannelId,
                    Start = programme.Start,
                    Minutes = (int)Math.Round(programme.Minutes),
                    Kind = c.IsMovie ? "Film" : c.IsSeries ? "Serie" : "Sonstiges",
                    Season = c.Season,
                    Episode = c.Episode,
                    Year = c.Year,
                    Categories = programme.Categories.ToList(),
                    Reasons = c.Reasons.ToList(),
                    ExtraSource = _provider.ExplainExtra(programme, channelName)
                });
            }

            result.Examples = examples;
            return Ok(result);
        }

        private static bool MatchesKind(EpgClassification c, string? kind) => (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "series" => c.IsSeries,
            "movie" => c.IsMovie,
            "other" => !c.IsSeries && !c.IsMovie,
            "kids" => c.IsKids,
            "news" => c.IsNews,
            "sports" => c.IsSports,
            _ => true
        };

        private void QueueGuideRefresh()
        {
            var worker = _tasks.ScheduledTasks.FirstOrDefault(t => string.Equals(t.ScheduledTask.Key, "RefreshGuide", StringComparison.Ordinal));
            if (worker != null)
            {
                _tasks.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
            }
        }

        /// <summary>
        /// Programme guide provider in Jellyfin's live TV settings.
        /// </summary>
        public class ProviderSummary
        {
            /// <summary>Gets or sets the ID.</summary>
            public string? Id { get; set; }

            /// <summary>Gets or sets the type ("xmltv", "schedulesdirect", "localrecs-epg").</summary>
            public string? Type { get; set; }

            /// <summary>Gets or sets the source.</summary>
            public string? Path { get; set; }

            /// <summary>Gets or sets the number of channel mappings.</summary>
            public int Mappings { get; set; }

            /// <summary>Gets or sets the position (1 = used first).</summary>
            public int Position { get; set; }

            /// <summary>Gets or sets a value indicating whether it is the LocalRecs EPG provider.</summary>
            public bool IsLocalRecs { get; set; }
        }

        /// <summary>
        /// Setup request.
        /// </summary>
        public class SetupRequest
        {
            /// <summary>Gets or sets the XMLTV source (URL or path); empty = take over from the other provider.</summary>
            public string? Path { get; set; }

            /// <summary>Gets or sets the provider whose source, tuners and channel mapping are taken over.</summary>
            public string? FromProviderId { get; set; }
        }

        /// <summary>
        /// Result of a rule test.
        /// </summary>
        public class TestResult
        {
            /// <summary>Gets or sets the source.</summary>
            public string? Source { get; set; }

            /// <summary>Gets or sets the number of channels.</summary>
            public int Channels { get; set; }

            /// <summary>Gets or sets the number of programmes.</summary>
            public int Programmes { get; set; }

            /// <summary>Gets or sets the number of series episodes.</summary>
            public int Series { get; set; }

            /// <summary>Gets or sets the number of series episodes with season/episode number.</summary>
            public int SeriesWithEpisodeNumber { get; set; }

            /// <summary>Gets or sets the number of movies.</summary>
            public int Movies { get; set; }

            /// <summary>Gets or sets the number of other programmes.</summary>
            public int Other { get; set; }

            /// <summary>Gets or sets the number of long programmes without movie evidence (teleshopping, sports, shows).</summary>
            public int LongWithoutMovieEvidence { get; set; }

            /// <summary>Gets or sets the number of programmes with movie hints from the „XMLTV-Quelle für Darsteller“.</summary>
            public int Enriched { get; set; }

            /// <summary>Gets or sets the number of kids programmes.</summary>
            public int Kids { get; set; }

            /// <summary>Gets or sets the number of news programmes.</summary>
            public int News { get; set; }

            /// <summary>Gets or sets the number of sports programmes.</summary>
            public int Sports { get; set; }

            /// <summary>Gets or sets the examples.</summary>
            public IReadOnlyList<TestProgramme> Examples { get; set; } = Array.Empty<TestProgramme>();

            /// <summary>Gets or sets the active rule lists (to check the saved settings).</summary>
            public IReadOnlyDictionary<string, string> Rules { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// Example programme of a rule test.
        /// </summary>
        public class TestProgramme
        {
            /// <summary>Gets or sets the title.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the sub-title.</summary>
            public string? SubTitle { get; set; }

            /// <summary>Gets or sets the channel.</summary>
            public string Channel { get; set; } = string.Empty;

            /// <summary>Gets or sets the start.</summary>
            public DateTimeOffset Start { get; set; }

            /// <summary>Gets or sets the length in minutes.</summary>
            public int Minutes { get; set; }

            /// <summary>Gets or sets the kind ("Film", "Serie", "Sonstiges").</summary>
            public string Kind { get; set; } = string.Empty;

            /// <summary>Gets or sets the season.</summary>
            public int? Season { get; set; }

            /// <summary>Gets or sets the episode.</summary>
            public int? Episode { get; set; }

            /// <summary>Gets or sets the year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets the categories.</summary>
            public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets the reasons.</summary>
            public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets what the second source („XMLTV-Quelle für Darsteller“) knows about the programme.</summary>
            public string? ExtraSource { get; set; }
        }
    }
}
