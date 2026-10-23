using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Moments;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// „Demnächst“: neue Folgen von Serien aus der Bibliothek (Sonarr) und vorgemerkte Filme, die bald ins Kino kommen oder
    /// digital bzw. auf Disc erscheinen (Radarr). Nur lesen – nichts wird angefragt oder vorgemerkt. Jeder sieht nur Serien aus seinen Bibliotheken
    /// und Filme, deren Radarr-Ordner zu einer seiner Filmbibliotheken passt, innerhalb seiner Altersfreigabe.
    /// </summary>
    public class UpcomingService
    {
        /// <summary>Most entries.</summary>
        public const int MaxItems = 30;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ErrorRetry = TimeSpan.FromMinutes(5);

        private readonly ILogger<UpcomingService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ArrClient _client;
        private readonly SemaphoreSlim _refresh = new SemaphoreSlim(1, 1);
        private Snapshot _snapshot = new Snapshot();

        /// <summary>
        /// Initializes a new instance of the <see cref="UpcomingService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="client">Sonarr/Radarr client, optional (for tests).</param>
        public UpcomingService(ILogger<UpcomingService> logger, ILibraryManager libraryManager, IUserDataManager userDataManager, ArrClient? client = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _client = client ?? new ArrClient();
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the day as text: „heute“, „morgen“ or the date with weekday („Fr. 24.10.“).
        /// </summary>
        /// <param name="day">The day.</param>
        /// <param name="today">Today.</param>
        /// <returns>The text.</returns>
        public static string DayText(DateOnly day, DateOnly today)
        {
            var days = day.DayNumber - today.DayNumber;
            if (days <= 0)
            {
                return "heute";
            }

            if (days == 1)
            {
                return "morgen";
            }

            var german = CultureInfo.GetCultureInfo("de-DE");
            return german.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek).TrimEnd('.') + ". " + day.ToString("dd.MM.", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the type of an episode date: series premiere (S01E01), season premiere (E01 of a later season) or new episode.
        /// </summary>
        /// <param name="season">Season number.</param>
        /// <param name="episode">Episode number.</param>
        /// <returns>The release type.</returns>
        public static string EpisodeType(int season, int episode)
        {
            if (episode == 1 && season == 1)
            {
                return UpcomingEntry.SeriesPremiere;
            }

            return episode == 1 && season > 1 ? UpcomingEntry.SeasonPremiere : UpcomingEntry.NewEpisode;
        }

        /// <summary>
        /// Gets the line for the card: „Neue Folge am Fr. 24.10.“, „Staffelstart heute“, „Serienstart morgen“, „Kinostart am …“,
        /// „Digital ab …“, „Auf Disc ab …“.
        /// </summary>
        /// <param name="releaseType">The release type.</param>
        /// <param name="day">The day.</param>
        /// <param name="today">Today.</param>
        /// <returns>The text.</returns>
        public static string Label(string releaseType, DateOnly day, DateOnly today)
        {
            var when = DayText(day, today);
            var on = when is "heute" or "morgen" ? when : "am " + when;
            return releaseType switch
            {
                UpcomingEntry.SeasonPremiere => "Staffelstart " + on,
                UpcomingEntry.SeriesPremiere => "Serienstart " + on,
                UpcomingEntry.Cinema => "Kinostart " + on,
                UpcomingEntry.Digital => "Digital ab " + when,
                UpcomingEntry.Physical => "Auf Disc ab " + when,
                _ => "Neue Folge " + on
            };
        }

        /// <summary>
        /// Gets the release of a movie that counts: the earliest of cinema, digital and disc release within the period.
        /// </summary>
        /// <param name="movie">The movie.</param>
        /// <param name="fromUtc">Start (UTC).</param>
        /// <param name="toUtc">End (UTC).</param>
        /// <returns>The release and its type, or null.</returns>
        public static (DateTime Date, string Type)? Release(ArrMovie movie, DateTime fromUtc, DateTime toUtc)
        {
            ArgumentNullException.ThrowIfNull(movie);
            var dates = new (DateTime? Date, string Type)[]
            {
                (movie.InCinemas, UpcomingEntry.Cinema),
                (movie.DigitalRelease, UpcomingEntry.Digital),
                (movie.PhysicalRelease, UpcomingEntry.Physical)
            };
            foreach (var (date, type) in dates.Where(d => d.Date is DateTime x && x >= fromUtc && x <= toUtc).OrderBy(d => d.Date))
            {
                return (date!.Value, type);
            }

            return null;
        }

        /// <summary>
        /// Gets the last part of a folder path (Windows or Unix), lower case.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The folder name, or empty.</returns>
        public static string FolderName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[^1].ToLowerInvariant();
        }

        /// <summary>
        /// Gets whether a Radarr movie may be shown to a user: the root folder must belong to one of the user's movie libraries
        /// (compared by folder name, since Radarr and Jellyfin often see different paths). If no library matches the folder,
        /// only users with all movie libraries see it.
        /// </summary>
        /// <param name="rootFolder">Radarr's root folder.</param>
        /// <param name="userLibraryFolders">Folder names of the user's movie libraries.</param>
        /// <param name="allLibraryFolders">Folder names of all movie libraries.</param>
        /// <param name="userHasAll">True if the user can see all movie libraries.</param>
        /// <returns>True if allowed.</returns>
        public static bool MovieAllowed(string? rootFolder, ISet<string> userLibraryFolders, ISet<string> allLibraryFolders, bool userHasAll)
        {
            ArgumentNullException.ThrowIfNull(userLibraryFolders);
            ArgumentNullException.ThrowIfNull(allLibraryFolders);
            if (userHasAll)
            {
                return true;
            }

            var name = FolderName(rootFolder);
            return name.Length > 0 && allLibraryFolders.Contains(name) && userLibraryFolders.Contains(name);
        }

        /// <summary>
        /// Gets the entries for a user, first series the user is watching or likes, then by date.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone, optional.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The entries.</returns>
        public async Task<IReadOnlyList<UpcomingEntry>> GetAsync(User user, int? utcOffsetMinutes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            var config = Config;
            if (!config.EnableUpcoming)
            {
                return Array.Empty<UpcomingEntry>();
            }

            var snapshot = await RefreshAsync(config, cancellationToken).ConfigureAwait(false);
            try
            {
                var offset = BehaviorService.ResolveOffset(utcOffsetMinutes);
                var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(offset));
                var result = Episodes(user, snapshot.Episodes, offset, today);
                result.AddRange(Movies(user, snapshot.Movies, today, config));

                // Das Nächste zuerst; am selben Tag Favoriten und gerade geschaute Serien vor
                return result
                    .OrderBy(e => e.Day)
                    .ThenBy(e => e.Priority ? 0 : 1)
                    .ThenBy(e => e.DateUtc)
                    .Take(MaxItems)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "„Demnächst“ konnte nicht zusammengestellt werden");
                return Array.Empty<UpcomingEntry>();
            }
        }

        /// <summary>
        /// Gets whether a Radarr movie of the current calendar has a poster or fanart (for the image endpoint).
        /// </summary>
        /// <param name="radarrId">Radarr's movie ID.</param>
        /// <returns>The movie, or null if not in the current calendar.</returns>
        public ArrMovie? KnownMovie(int radarrId) => _snapshot.Movies.FirstOrDefault(m => m.RadarrId == radarrId);

        /// <summary>
        /// Gets a poster or fanart of a movie of the current calendar from Radarr (cached for the calendar's lifetime).
        /// </summary>
        /// <param name="radarrId">Radarr's movie ID.</param>
        /// <param name="fanart">True for fanart.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The image, or null.</returns>
        public async Task<(byte[] Data, string ContentType)?> GetImageAsync(int radarrId, bool fanart, CancellationToken cancellationToken)
        {
            var snapshot = _snapshot;
            var movie = snapshot.Movies.FirstOrDefault(m => m.RadarrId == radarrId);
            var baseUrl = ArrClient.BaseUrl(Config.RadarrUrl);
            if (movie == null || baseUrl == null || string.IsNullOrWhiteSpace(Config.RadarrApiKey) || !(fanart ? movie.HasFanart : movie.HasPoster))
            {
                return null;
            }

            var key = (radarrId, fanart);
            lock (snapshot.Images)
            {
                if (snapshot.Images.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            (byte[] Data, string ContentType)? image = null;
            try
            {
                image = await _client.GetImageAsync(baseUrl, Config.RadarrApiKey.Trim(), radarrId, fanart, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogDebug("Radarr-Bild nicht abrufbar: {Type}", ex.GetType().Name);
            }

            lock (snapshot.Images)
            {
                if (snapshot.Images.Count < 200)
                {
                    snapshot.Images[key] = image;
                }
            }

            return image;
        }

        /// <summary>
        /// Checks the connection to Sonarr and Radarr (for the settings page); never returns the keys.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Status texts.</returns>
        public async Task<(string Sonarr, string Radarr)> CheckAsync(CancellationToken cancellationToken)
        {
            var config = Config;
            var now = DateTime.UtcNow;
            var sonarr = await CheckOneAsync(config.SonarrUrl, config.SonarrApiKey, async (url, key) =>
                (await _client.GetEpisodesAsync(url, key, now.Date, now.Date.AddDays(Math.Clamp(config.UpcomingSeriesDays, 1, 60)), cancellationToken).ConfigureAwait(false)).Count + " kommende Folgen").ConfigureAwait(false);
            var radarr = await CheckOneAsync(config.RadarrUrl, config.RadarrApiKey, async (url, key) =>
                (await _client.GetMoviesAsync(url, key, now.Date, now.Date.AddDays(Math.Clamp(config.UpcomingMovieDays, 1, 90)), cancellationToken).ConfigureAwait(false)).Count + " vorgemerkte Filme im Zeitraum").ConfigureAwait(false);
            _snapshot = new Snapshot();
            return (sonarr, radarr);
        }

        /// <summary>
        /// Forgets the cached calendars (after the settings changed).
        /// </summary>
        public void Invalidate() => _snapshot = new Snapshot();

        private static async Task<string> CheckOneAsync(string? url, string? key, Func<string, string, Task<string>> check)
        {
            var baseUrl = ArrClient.BaseUrl(url);
            if (baseUrl == null)
            {
                return string.IsNullOrWhiteSpace(url) ? "nicht eingerichtet" : "Adresse ungültig (http:// oder https:// mit Port, ohne Zugangsdaten)";
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return "API-Schlüssel fehlt";
            }

            try
            {
                return "verbunden, " + await check(baseUrl, key.Trim()).ConfigureAwait(false);
            }
            catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return "API-Schlüssel abgelehnt (401)";
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is System.Text.Json.JsonException)
            {
                return "nicht erreichbar (" + (ex is TaskCanceledException ? "Zeitüberschreitung" : ex is System.Text.Json.JsonException ? "keine gültige Antwort" : ex.Message) + ")";
            }
        }

        private async Task<Snapshot> RefreshAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            var snapshot = _snapshot;
            var sonarr = ArrClient.BaseUrl(config.SonarrUrl);
            var radarr = ArrClient.BaseUrl(config.RadarrUrl);
            var signature = (sonarr ?? string.Empty) + "|" + (radarr ?? string.Empty) + "|" + config.UpcomingSeriesDays + "|" + config.UpcomingMovieDays;
            if (snapshot.Signature == signature && DateTime.UtcNow < snapshot.ValidUntil)
            {
                return snapshot;
            }

            await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                snapshot = _snapshot;
                if (snapshot.Signature == signature && DateTime.UtcNow < snapshot.ValidUntil)
                {
                    return snapshot;
                }

                var fresh = new Snapshot { Signature = signature, ValidUntil = DateTime.UtcNow.Add(CacheDuration) };
                var start = DateTime.UtcNow.Date.AddDays(-1);
                if (sonarr != null && !string.IsNullOrWhiteSpace(config.SonarrApiKey))
                {
                    try
                    {
                        fresh.Episodes = await _client.GetEpisodesAsync(sonarr, config.SonarrApiKey.Trim(), start, start.AddDays(Math.Clamp(config.UpcomingSeriesDays, 1, 60) + 2), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.Text.Json.JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                    {
                        _logger.LogWarning("„Demnächst“: Sonarr nicht abrufbar ({Type}: {Message})", ex.GetType().Name, ex.Message);
                        fresh.ValidUntil = DateTime.UtcNow.Add(ErrorRetry);
                    }
                }

                if (radarr != null && !string.IsNullOrWhiteSpace(config.RadarrApiKey))
                {
                    try
                    {
                        fresh.Movies = await _client.GetMoviesAsync(radarr, config.RadarrApiKey.Trim(), start, start.AddDays(Math.Clamp(config.UpcomingMovieDays, 1, 90) + 2), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.Text.Json.JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                    {
                        _logger.LogWarning("„Demnächst“: Radarr nicht abrufbar ({Type}: {Message})", ex.GetType().Name, ex.Message);
                        fresh.ValidUntil = DateTime.UtcNow.Add(ErrorRetry);
                    }
                }

                _snapshot = fresh;
                return fresh;
            }
            finally
            {
                _refresh.Release();
            }
        }

        // Je Serie die nächste Folge (weitere im Zeitraum werden gezählt); nur Serien, die der Benutzer in Jellyfin sehen darf
        private List<UpcomingEntry> Episodes(User user, IReadOnlyList<ArrEpisode> episodes, int offset, DateOnly today)
        {
            var result = new List<UpcomingEntry>();
            var startUtc = today.ToDateTime(TimeOnly.MinValue).AddMinutes(-offset);
            var endUtc = startUtc.AddDays(Math.Clamp(Config.UpcomingSeriesDays, 1, 60));
            var upcoming = episodes.Where(e => e.AirDateUtc >= startUtc && e.AirDateUtc < endUtc).ToList();
            if (upcoming.Count == 0)
            {
                return result;
            }

            var series = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            });
            var byProvider = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
            var byTitle = new Dictionary<string, BaseItem>(StringComparer.Ordinal);
            foreach (var item in series)
            {
                foreach (var (provider, id) in item.ProviderIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        byProvider.TryAdd(provider + ":" + id, item);
                    }
                }

                byTitle.TryAdd(MomentsService.NormalizeTitle(item.Name), item);
            }

            var priority = PrioritySeries(user, series);
            foreach (var group in upcoming.GroupBy(e => e.SonarrSeriesId))
            {
                var first = group.OrderBy(e => e.AirDateUtc).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).First();
                var match = (first.TvdbId != null && byProvider.TryGetValue("Tvdb:" + first.TvdbId, out var tvdb) ? tvdb : null)
                    ?? (first.TmdbId != null && byProvider.TryGetValue("Tmdb:" + first.TmdbId, out var tmdb) ? tmdb : null)
                    ?? (first.ImdbId != null && byProvider.TryGetValue("Imdb:" + first.ImdbId, out var imdb) ? imdb : null)
                    ?? (byTitle.TryGetValue(MomentsService.NormalizeTitle(first.SeriesTitle), out var byName) ? byName : null);
                if (match == null)
                {
                    continue;
                }

                var day = DateOnly.FromDateTime(first.AirDateUtc.AddMinutes(offset));
                var more = group.Count() - 1;
                var type = EpisodeType(first.SeasonNumber, first.EpisodeNumber);
                result.Add(new UpcomingEntry
                {
                    Kind = UpcomingEntry.EpisodeKind,
                    ReleaseType = type,
                    Day = day,
                    EpisodeOverview = first.Overview,
                    Network = first.Network,
                    Title = match.Name ?? first.SeriesTitle,
                    DateUtc = first.AirDateUtc,
                    SeriesItem = match,
                    SeasonNumber = first.SeasonNumber,
                    EpisodeNumber = first.EpisodeNumber,
                    EpisodeTitle = first.Title,
                    MoreEpisodes = more,
                    Priority = priority.Contains(match.Id),
                    Subtitle = Label(type, day, today)
                });
            }

            return result;
        }

        // Favoriten und Serien, von denen in den letzten 90 Tagen etwas gelaufen ist
        private HashSet<Guid> PrioritySeries(User user, IReadOnlyList<BaseItem> series)
        {
            var result = new HashSet<Guid>();
            var lookup = UserDataLookup.Load(_userDataManager, user, series);
            foreach (var item in series)
            {
                if (lookup.Get(item)?.IsFavorite == true)
                {
                    result.Add(item.Id);
                }
            }

            foreach (var episode in _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = 300
            }).OfType<Episode>())
            {
                var played = _userDataManager.GetUserData(user, episode)?.LastPlayedDate;
                if (played is DateTime date && date > DateTime.UtcNow.AddDays(-90))
                {
                    result.Add(episode.SeriesId);
                }
            }

            return result;
        }

        private List<UpcomingEntry> Movies(User user, IReadOnlyList<ArrMovie> movies, DateOnly today, PluginConfiguration config)
        {
            var result = new List<UpcomingEntry>();
            var fromUtc = today.ToDateTime(TimeOnly.MinValue).AddDays(-1);
            var toUtc = today.ToDateTime(TimeOnly.MinValue).AddDays(Math.Clamp(config.UpcomingMovieDays, 1, 90) + 1);
            var candidates = movies.Select(m => (Movie: m, Release: Release(m, fromUtc, toUtc))).Where(m => m.Release != null).ToList();
            if (candidates.Count == 0)
            {
                return result;
            }

            // Filmbibliotheken: alle und die des Benutzers (Ordnernamen)
            var all = new HashSet<string>(StringComparer.Ordinal);
            var mine = new HashSet<string>(StringComparer.Ordinal);
            var allMovieLibraries = 0;
            var myMovieLibraries = 0;
            var root = _libraryManager.GetUserRootFolder();
            var visible = root.GetChildren(user, true).Select(f => f.Id).ToHashSet();
            foreach (var library in root.Children.OfType<Folder>())
            {
                var type = (library as ICollectionFolder)?.CollectionType?.ToString();
                if (type != null && !string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                allMovieLibraries++;
                var own = visible.Contains(library.Id);
                if (own)
                {
                    myMovieLibraries++;
                }

                foreach (var location in library.PhysicalLocations)
                {
                    var name = FolderName(location);
                    all.Add(name);
                    if (own)
                    {
                        mine.Add(name);
                    }
                }
            }

            if (myMovieLibraries == 0)
            {
                return result;
            }

            var owned = _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true, IsVirtualItem = false })
                .Select(m => m.GetProviderId(MetadataProvider.Tmdb))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (movie, release) in candidates)
            {
                if ((movie.TmdbId != null && owned.Contains(movie.TmdbId))
                    || !MovieAllowed(movie.RootFolderPath, mine, all, myMovieLibraries == allMovieLibraries))
                {
                    continue;
                }

                // Altersfreigabe wie bei Titeln der Bibliothek
                var probe = new Movie { Name = movie.Title, OfficialRating = movie.Certification };
                if (!probe.IsParentalAllowed(user, true))
                {
                    continue;
                }

                var (date, type) = release!.Value;
                var day = DateOnly.FromDateTime(date);
                result.Add(new UpcomingEntry
                {
                    Kind = UpcomingEntry.MovieKind,
                    ReleaseType = type,
                    Day = day,
                    Title = movie.Title,
                    DateUtc = date,
                    Movie = movie,
                    Subtitle = Label(type, day, today)
                });
            }

            return result;
        }

        private sealed class Snapshot
        {
            public string Signature { get; set; } = string.Empty;

            public DateTime ValidUntil { get; set; }

            public IReadOnlyList<ArrEpisode> Episodes { get; set; } = Array.Empty<ArrEpisode>();

            public IReadOnlyList<ArrMovie> Movies { get; set; } = Array.Empty<ArrMovie>();

            public Dictionary<(int Id, bool Fanart), (byte[] Data, string ContentType)?> Images { get; } = new Dictionary<(int Id, bool Fanart), (byte[] Data, string ContentType)?>();
        }
    }
}
