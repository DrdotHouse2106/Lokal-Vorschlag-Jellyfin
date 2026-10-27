using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Jellyfin.Plugin.LocalRecs.Upcoming;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Discover
{
    /// <summary>
    /// Entdecken-Reihen aus TMDB, für Web und Apps gleich: „Demnächst im Kino“ (deutsche Kinostarts der nächsten Wochen),
    /// „Neu dieses Jahr – Filme“ und „Neu dieses Jahr – Serien“. Nur lesend, es wird nichts angefragt. Sechs Stunden
    /// zwischengespeichert; je Benutzer wird markiert, was schon in seinen Bibliotheken liegt, und die Altersfreigabe geprüft.
    /// </summary>
    public class DiscoverService
    {
        /// <summary>Key of „Demnächst im Kino“.</summary>
        public const string CinemaKey = "cinema";

        /// <summary>Key of „Neu dieses Jahr – Filme“.</summary>
        public const string NewMoviesKey = "newmovies";

        /// <summary>Key of „Neu dieses Jahr – Serien“.</summary>
        public const string NewSeriesKey = "newseries";

        /// <summary>Most titles per row.</summary>
        public const int MaxItems = 20;

        /// <summary>Weeks ahead for cinema starts.</summary>
        public const int CinemaDays = 56;

        /// <summary>Minimum votes of released movies in „Neu dieses Jahr“.</summary>
        public const int MinMovieVotes = 25;

        /// <summary>Minimum votes of released series in „Neu dieses Jahr“.</summary>
        public const int MinSeriesVotes = 10;

        /// <summary>Minimum popularity of titles not yet released.</summary>
        public const double MinUpcomingPopularity = 10;

        private const string TmdbBase = "https://api.themoviedb.org/3/";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
        private static readonly TimeSpan ErrorRetry = TimeSpan.FromMinutes(15);

        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly ILogger<DiscoverService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerConfigurationManager? _serverConfig;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly SemaphoreSlim _refresh = new SemaphoreSlim(1, 1);
        private (string Signature, DateTime ValidUntil, IReadOnlyList<Row> Rows)? _snapshot;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoverService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="serverConfig">Server configuration (metadata language and country), optional.</param>
        /// <param name="send">Sends a request (for tests), optional.</param>
        public DiscoverService(ILogger<DiscoverService> logger, ILibraryManager libraryManager, IServerConfigurationManager? serverConfig = null, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _serverConfig = serverConfig;
            _send = send ?? ((request, ct) => SharedClient.SendAsync(request, ct));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Builds „Demnächst im Kino“: German cinema start within <see cref="CinemaDays"/> days from today, soonest first.
        /// </summary>
        /// <param name="details">Parsed details with release dates.</param>
        /// <param name="today">Today.</param>
        /// <returns>The items.</returns>
        public static List<DiscoverItem> BuildCinema(IEnumerable<(DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases)> details, DateOnly today)
        {
            ArgumentNullException.ThrowIfNull(details);
            var result = new List<(DiscoverItem Item, DateOnly Day)>();
            foreach (var (item, releases) in details)
            {
                if (TmdbDiscover.CinemaStart(releases) is DateOnly day && day >= today && day <= today.AddDays(CinemaDays) && TmdbDiscover.IsRelevant(item, false, 0, 0))
                {
                    item.ReleaseDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    item.Released = day <= today;
                    item.ReleaseText = UpcomingService.Label(UpcomingEntry.Cinema, day, today);
                    result.Add((item, day));
                }
            }

            return result.OrderBy(r => r.Day).ThenByDescending(r => r.Item.Popularity).Select(r => r.Item).DistinctBy(i => i.TmdbId).Take(MaxItems).ToList();
        }

        /// <summary>
        /// Builds „Neu dieses Jahr“: date in the year (movies: German release, series: first air date), only relevant titles
        /// (<see cref="TmdbDiscover.IsRelevant"/>), ordered by a mix of popularity and closeness of the date to today.
        /// </summary>
        /// <param name="details">Parsed details with release dates.</param>
        /// <param name="today">Today.</param>
        /// <param name="series">True for series.</param>
        /// <param name="exclude">TMDB IDs already shown in another row (e.g. „Demnächst im Kino“), optional.</param>
        /// <returns>The items.</returns>
        public static List<DiscoverItem> BuildNewThisYear(IEnumerable<(DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases)> details, DateOnly today, bool series, ISet<int>? exclude = null)
        {
            ArgumentNullException.ThrowIfNull(details);
            var result = new List<(DiscoverItem Item, DateOnly Day)>();
            foreach (var (item, releases) in details)
            {
                if (exclude != null && exclude.Contains(item.TmdbId))
                {
                    continue;
                }

                var day = series
                    ? releases.Where(r => r.Type == 0).Select(r => (DateOnly?)r.Date).FirstOrDefault()
                    : TmdbDiscover.LocalRelease(releases);
                if (day is not DateOnly date || date.Year != today.Year
                    || !TmdbDiscover.IsRelevant(item, date <= today, series ? MinSeriesVotes : MinMovieVotes, MinUpcomingPopularity))
                {
                    continue;
                }

                item.ReleaseDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                item.Released = date <= today;
                item.ReleaseText = TmdbDiscover.NewText(date, today, series);
                result.Add((item, date));
            }

            return result
                .OrderByDescending(r => TmdbDiscover.Score(r.Item, r.Day, today))
                .ThenBy(r => r.Day)
                .Select(r => r.Item)
                .DistinctBy(i => i.TmdbId)
                .Take(MaxItems)
                .ToList();
        }

        /// <summary>
        /// Gets the rows for a user: copies with library state, without titles above the user's parental rating.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="key">Only this row, optional.</param>
        /// <returns>The rows that are not empty.</returns>
        public async Task<IReadOnlyList<Row>> GetAsync(User user, CancellationToken cancellationToken, string? key = null)
        {
            ArgumentNullException.ThrowIfNull(user);
            var config = Config;
            if (!config.EnableDiscoverRows)
            {
                return Array.Empty<Row>();
            }

            var rows = await RowsAsync(config, cancellationToken).ConfigureAwait(false);
            var library = LibraryIds(user);
            var result = new List<Row>();
            foreach (var row in rows.Where(r => key == null || string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                var items = new List<DiscoverItem>();
                foreach (var source in row.Items)
                {
                    var item = source.Copy();
                    if (library.TryGetValue((item.MediaType, item.TmdbId.ToString(CultureInfo.InvariantCulture)), out var id))
                    {
                        item.InLibrary = true;
                        item.JellyfinId = id.ToString("N");
                    }

                    // Altersfreigabe wie bei Titeln der Bibliothek
                    BaseItem probe = item.MediaType == "tv" ? new Series { Name = item.Title, OfficialRating = item.OfficialRating } : new Movie { Name = item.Title, OfficialRating = item.OfficialRating };
                    if (probe.IsParentalAllowed(user, true))
                    {
                        items.Add(item);
                    }
                }

                if (items.Count > 0)
                {
                    result.Add(new Row { Key = row.Key, Title = row.Title, Items = items });
                }
            }

            return result;
        }

        private static async Task<List<T>> InParallel<TIn, T>(IEnumerable<TIn> inputs, int parallel, Func<TIn, Task<T?>> work)
            where T : struct
        {
            using var gate = new SemaphoreSlim(parallel);
            var tasks = inputs.Select(async input =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    return await work(input).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            var done = await Task.WhenAll(tasks).ConfigureAwait(false);
            return done.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        }

        // TMDB-IDs der Filme und Serien, die der Benutzer sehen darf
        private Dictionary<(string Type, string TmdbId), Guid> LibraryIds(User user)
        {
            var result = new Dictionary<(string Type, string TmdbId), Guid>();
            foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            }))
            {
                var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
                if (!string.IsNullOrEmpty(tmdb))
                {
                    result.TryAdd((item is Series ? "tv" : "movie", tmdb), item.Id);
                }
            }

            return result;
        }

        private async Task<IReadOnlyList<Row>> RowsAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            var (key, _) = ExternalSimilarityService.ResolveTmdbKeyWithSource(config);
            if (string.IsNullOrEmpty(key))
            {
                return Array.Empty<Row>();
            }

            var server = _serverConfig?.Configuration;
            var country = string.IsNullOrWhiteSpace(server?.MetadataCountryCode) ? "DE" : server!.MetadataCountryCode.ToUpperInvariant();
            var language = (string.IsNullOrWhiteSpace(server?.PreferredMetadataLanguage) ? "de" : server!.PreferredMetadataLanguage) + "-" + country;
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(Behavior.BehaviorService.ResolveOffset(null)));
            var signature = country + "|" + language + "|" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_snapshot is { } cached && cached.Signature == signature && DateTime.UtcNow < cached.ValidUntil)
            {
                return cached.Rows;
            }

            await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_snapshot is { } again && again.Signature == signature && DateTime.UtcNow < again.ValidUntil)
                {
                    return again.Rows;
                }

                var failed = false;
                async Task<List<(int Id, double Popularity)>> List(string path)
                {
                    var json = await GetAsync(key, path, cancellationToken).ConfigureAwait(false);
                    if (json == null)
                    {
                        failed = true;
                        return new List<(int Id, double Popularity)>();
                    }

                    return TmdbDiscover.ParseList(json);
                }

                async Task<List<(DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases)>> Details(IEnumerable<int> ids, bool series)
                {
                    var append = series ? "content_ratings,videos,watch/providers" : "release_dates,videos,watch/providers";
                    return await InParallel(ids, 4, async id =>
                    {
                        var json = await GetAsync(key, (series ? "tv/" : "movie/") + id.ToString(CultureInfo.InvariantCulture) + "?language=" + language + "&append_to_response=" + append + "&include_video_language=" + language.Split('-')[0] + ",en", cancellationToken).ConfigureAwait(false);
                        return json == null ? null : TmdbDiscover.ParseDetails(json, series, country);
                    }).ConfigureAwait(false);
                }

                var year = today.Year.ToString(CultureInfo.InvariantCulture);
                var cinemaIds = new List<(int Id, double Popularity)>();
                for (var page = 1; page <= 3; page++)
                {
                    cinemaIds.AddRange(await List("movie/upcoming?language=" + language + "&region=" + country + "&page=" + page).ConfigureAwait(false));
                }

                var movieIds = new List<(int Id, double Popularity)>();
                var seriesIds = new List<(int Id, double Popularity)>();
                for (var page = 1; page <= 3; page++)
                {
                    movieIds.AddRange(await List("discover/movie?language=" + language + "&region=" + country + "&primary_release_year=" + year + "&sort_by=popularity.desc&include_adult=false&page=" + page).ConfigureAwait(false));
                    seriesIds.AddRange(await List("discover/tv?language=" + language + "&first_air_date_year=" + year + "&sort_by=popularity.desc&include_adult=false&include_null_first_air_dates=false&page=" + page).ConfigureAwait(false));
                }

                var cinema = BuildCinema(await Details(cinemaIds.OrderByDescending(i => i.Popularity).Select(i => i.Id).Distinct().Take(40), false).ConfigureAwait(false), today);

                // Kinostarts stehen in „Demnächst im Kino“, nicht noch einmal unter „Neu dieses Jahr“
                var movies = BuildNewThisYear(await Details(movieIds.Select(i => i.Id).Distinct().Take(45), false).ConfigureAwait(false), today, false, cinema.Select(i => i.TmdbId).ToHashSet());
                var series = BuildNewThisYear(await Details(seriesIds.Select(i => i.Id).Distinct().Take(45), true).ConfigureAwait(false), today, true);
                var rows = new List<Row>
                {
                    new Row { Key = CinemaKey, Title = "Demnächst im Kino", Items = cinema },
                    new Row { Key = NewMoviesKey, Title = "Neu dieses Jahr – Filme", Items = movies },
                    new Row { Key = NewSeriesKey, Title = "Neu dieses Jahr – Serien", Items = series }
                };
                var empty = rows.All(r => r.Items.Count == 0);
                _snapshot = (signature, DateTime.UtcNow.Add(failed && empty ? ErrorRetry : CacheDuration), rows);
                return rows;
            }
            finally
            {
                _refresh.Release();
            }
        }

        private async Task<string?> GetAsync(string key, string path, CancellationToken cancellationToken)
        {
            var url = TmdbBase + path;
            var bearer = key.Length > 40;
            using var request = new HttpRequestMessage(HttpMethod.Get, bearer ? url : url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api_key=" + Uri.EscapeDataString(key));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (bearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            try
            {
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Nur der Typ – die Adresse kann den Schlüssel enthalten
                _logger.LogDebug("Entdecken: TMDB nicht erreichbar ({Type})", ex.GetType().Name);
                return null;
            }
        }

        /// <summary>A row.</summary>
        public class Row
        {
            /// <summary>Gets or sets the key: „cinema“, „newmovies“ or „newseries“.</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the titles.</summary>
            public IReadOnlyList<DiscoverItem> Items { get; set; } = Array.Empty<DiscoverItem>();

            /// <summary>Gets the number of titles.</summary>
            public int TotalRecordCount => Items.Count;
        }
    }
}
