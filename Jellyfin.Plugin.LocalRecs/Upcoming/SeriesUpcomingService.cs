using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Moments;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// Kommende Folgen einer einzelnen Serie für die Detailseite: die nächste in Sonarr bekannte Folge (ohne Zeitgrenze),
    /// weitere in den 30 Tagen danach und die heute erschienenen, schon importierten Folgen. Nur lesend.
    /// </summary>
    public class SeriesUpcomingService
    {
        private static readonly TimeSpan SeriesListDuration = TimeSpan.FromHours(6);
        private static readonly TimeSpan EpisodesDuration = TimeSpan.FromMinutes(30);

        private readonly ILogger<SeriesUpcomingService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ArrClient _client;
        private readonly SemaphoreSlim _listLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, (DateTime Created, List<ArrEpisode> Episodes)> _episodes = new ConcurrentDictionary<int, (DateTime Created, List<ArrEpisode> Episodes)>();
        private (string Url, DateTime Created, List<ArrEpisode> Series)? _seriesList;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeriesUpcomingService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="client">Sonarr client, optional (for tests).</param>
        public SeriesUpcomingService(ILogger<SeriesUpcomingService> logger, ILibraryManager libraryManager, ArrClient? client = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _client = client ?? new ArrClient();
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the line for the detail page: „Neue Folge heute“ / „Nächste Folge am Fr. 31.10.“ / „Staffelstart morgen“, then time,
        /// network and SxxEyy with title.
        /// </summary>
        /// <param name="episode">The episode.</param>
        /// <param name="clock">The app's clock.</param>
        /// <param name="today">Today.</param>
        /// <returns>The text.</returns>
        public static string Text(SeriesUpcoming.SeriesEpisode episode, LocalClock clock, DateOnly today)
        {
            ArgumentNullException.ThrowIfNull(episode);
            ArgumentNullException.ThrowIfNull(clock);
            var day = clock.Day(episode.Date);
            var days = day.DayNumber - today.DayNumber;
            var head = episode.ReleaseType == UpcomingEntry.NewEpisode && days > 1
                ? "Nächste Folge am " + UpcomingService.DayText(day, today)
                : UpcomingService.Label(episode.ReleaseType, day, today);
            var parts = new List<string> { head, clock.TimeText(episode.Date) };
            if (!string.IsNullOrWhiteSpace(episode.Network))
            {
                parts.Add(episode.Network);
            }

            parts.Add(Number(episode.SeasonNumber, episode.EpisodeNumber) + (string.IsNullOrWhiteSpace(episode.EpisodeTitle) ? string.Empty : " " + episode.EpisodeTitle));
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Gets the episode number as „S02E05“.
        /// </summary>
        /// <param name="season">Season number.</param>
        /// <param name="episode">Episode number.</param>
        /// <returns>The text.</returns>
        public static string Number(int season, int episode) => string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", season, episode);

        /// <summary>
        /// Builds the result from Sonarr's episodes of a series.
        /// </summary>
        /// <param name="episodes">All episodes of the series.</param>
        /// <param name="network">Network of the series.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="clock">The app's clock.</param>
        /// <param name="itemIds">Jellyfin IDs of the episodes in the library by (season, episode).</param>
        /// <returns>The result.</returns>
        public static SeriesUpcoming Build(IReadOnlyList<ArrEpisode> episodes, string? network, DateTime nowUtc, LocalClock clock, IReadOnlyDictionary<(int Season, int Episode), Guid> itemIds)
        {
            ArgumentNullException.ThrowIfNull(episodes);
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(itemIds);
            var today = clock.Day(nowUtc);
            var startOfToday = clock.StartOfDayUtc(today);
            var result = new SeriesUpcoming();
            SeriesUpcoming.SeriesEpisode Info(ArrEpisode e) => new SeriesUpcoming.SeriesEpisode
            {
                SeasonNumber = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                EpisodeTitle = e.Title,
                EpisodeOverview = e.Overview,
                Date = e.AirDateUtc,
                LocalDate = clock.Day(e.AirDateUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LocalTime = clock.TimeText(e.AirDateUtc),
                ReleaseType = UpcomingService.EpisodeType(e.SeasonNumber, e.EpisodeNumber),
                Network = network,
                ItemId = itemIds.TryGetValue((e.SeasonNumber, e.EpisodeNumber), out var id) ? id.ToString("N") : null
            };

            // Specials (Staffel 0) nur, wenn sonst nichts kommt
            var upcoming = episodes
                .Where(e => !e.HasFile && e.AirDateUtc >= startOfToday)
                .OrderBy(e => e.SeasonNumber == 0 ? 1 : 0)
                .ThenBy(e => e.AirDateUtc)
                .ThenBy(e => e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber)
                .ToList();
            var next = upcoming.FirstOrDefault();
            if (next != null)
            {
                result.Next = Info(next);
                result.Text = Text(result.Next, clock, today);
                var more = upcoming.Skip(1).Where(e => e.SeasonNumber != 0 && e.AirDateUtc <= next.AirDateUtc.AddDays(30)).OrderBy(e => e.AirDateUtc).ToList();
                if (more.Count > 0)
                {
                    var until = clock.Day(more[^1].AirDateUtc);
                    result.MoreEpisodes = more.Count;
                    result.MoreUntil = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    result.MoreText = "+" + more.Count + (more.Count == 1 ? " weitere am " : " weitere bis ") + until.ToString("dd.MM.", CultureInfo.InvariantCulture);
                }
            }

            // Heute erschienen und schon importiert
            var fresh = episodes
                .Where(e => e.HasFile && e.AirDateUtc >= startOfToday && e.AirDateUtc <= nowUtc.AddHours(12))
                .OrderBy(e => e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber)
                .Select(Info)
                .ToList();
            result.NewToday = fresh;
            result.NewText = fresh.Count == 0 ? string.Empty : "Neu: " + string.Join(", ", fresh.Select(e => Number(e.SeasonNumber, e.EpisodeNumber)));
            return result;
        }

        /// <summary>
        /// Gets what comes next for a series the user can see; null if the series is unknown, not visible, Sonarr is not set up
        /// or does not know the series.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="seriesId">Jellyfin ID of the series.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone, optional.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="timeZone">IANA time zone of the app, optional.</param>
        /// <returns>The result, or null.</returns>
        public async Task<SeriesUpcoming?> GetAsync(User user, Guid seriesId, int? utcOffsetMinutes, CancellationToken cancellationToken, string? timeZone = null)
        {
            ArgumentNullException.ThrowIfNull(user);
            var config = Config;
            var baseUrl = ArrClient.BaseUrl(config.SonarrUrl);
            if (!config.EnableUpcoming || baseUrl == null || string.IsNullOrWhiteSpace(config.SonarrApiKey)
                || _libraryManager.GetItemById(seriesId) is not Series series || !series.IsVisibleStandalone(user))
            {
                return null;
            }

            var key = config.SonarrApiKey.Trim();
            try
            {
                var list = await SeriesListAsync(baseUrl, key, cancellationToken).ConfigureAwait(false);
                var sonarr = Match(series, list);
                if (sonarr == null)
                {
                    return null;
                }

                if (!_episodes.TryGetValue(sonarr.SonarrSeriesId, out var cached) || DateTime.UtcNow - cached.Created > EpisodesDuration)
                {
                    cached = (DateTime.UtcNow, await _client.GetSeriesEpisodesAsync(baseUrl, key, sonarr.SonarrSeriesId, cancellationToken).ConfigureAwait(false));
                    if (_episodes.Count > 500)
                    {
                        _episodes.Clear();
                    }

                    _episodes[sonarr.SonarrSeriesId] = cached;
                }

                var itemIds = new Dictionary<(int Season, int Episode), Guid>();
                foreach (var episode in _libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    AncestorIds = new[] { series.Id },
                    Recursive = true,
                    IsVirtualItem = false
                }).OfType<Episode>())
                {
                    if (episode.ParentIndexNumber is int season && episode.IndexNumber is int number)
                    {
                        itemIds.TryAdd((season, number), episode.Id);
                    }
                }

                return Build(cached.Episodes, sonarr.Network, DateTime.UtcNow, LocalClock.Create(timeZone, utcOffsetMinutes, BehaviorService.ResolveOffset(null)), itemIds);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.Text.Json.JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogWarning("Kommende Folgen: Sonarr nicht abrufbar ({Type}: {Message})", ex.GetType().Name, ex.Message);
                return null;
            }
        }

        private static ArrEpisode? Match(Series series, IReadOnlyList<ArrEpisode> list)
        {
            foreach (var (provider, id) in series.ProviderIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var match = provider switch
                {
                    "Tvdb" => list.FirstOrDefault(s => s.TvdbId == id),
                    "Tmdb" => list.FirstOrDefault(s => s.TmdbId == id),
                    "Imdb" => list.FirstOrDefault(s => string.Equals(s.ImdbId, id, StringComparison.OrdinalIgnoreCase)),
                    _ => null
                };
                if (match != null)
                {
                    return match;
                }
            }

            var title = MomentsService.NormalizeTitle(series.Name);
            return title.Length == 0 ? null : list.FirstOrDefault(s => MomentsService.NormalizeTitle(s.SeriesTitle) == title);
        }

        private async Task<List<ArrEpisode>> SeriesListAsync(string baseUrl, string key, CancellationToken cancellationToken)
        {
            if (_seriesList is { } cached && cached.Url == baseUrl && DateTime.UtcNow - cached.Created < SeriesListDuration)
            {
                return cached.Series;
            }

            await _listLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_seriesList is { } again && again.Url == baseUrl && DateTime.UtcNow - again.Created < SeriesListDuration)
                {
                    return again.Series;
                }

                var list = await _client.GetSeriesListAsync(baseUrl, key, cancellationToken).ConfigureAwait(false);
                _seriesList = (baseUrl, DateTime.UtcNow, list);
                return list;
            }
            finally
            {
                _listLock.Release();
            }
        }
    }
}
