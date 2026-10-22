using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Worlds;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Collections
{
    /// <summary>
    /// „Filmreihen für dich“: Reihen (TMDB-Sammlungen) mit mindestens zwei Teilen in den Bibliotheken des Benutzers.
    /// Zuerst angefangene Reihen mit dem nächsten ungesehenen Teil (zuletzt Gesehenes zuerst), danach nicht angefangene
    /// Reihen, die zum Geschmack passen. Reihen, die schon eine Welt bilden (z. B. James Bond), kommen zuletzt.
    /// Woher die Zuordnung kommt: die TMDB-Sammlung am Film („TmdbCollection“), Jellyfins Sammlungen mit TMDB-ID oder die
    /// nächtliche Abfrage bei TMDB (<see cref="MovieSeriesLookup"/>).
    /// </summary>
    public class MovieSeriesService
    {
        /// <summary>Minimum number of parts in the libraries.</summary>
        public const int MinParts = 2;

        private static readonly TimeSpan MembershipCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan UserCacheDuration = TimeSpan.FromMinutes(5);

        private readonly ILogger<MovieSeriesService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly MovieSeriesLookup _lookup;
        private readonly WorldService _worlds;
        private readonly RecommendationStore _store;
        private readonly object _membershipLock = new object();
        private readonly ConcurrentDictionary<Guid, (DateTime Created, string Access, IReadOnlyList<MovieSeries> Series)> _userCache = new();
        private (DateTime Created, long Version, Dictionary<string, (string Name, List<Guid> Movies)> Members)? _membership;

        /// <summary>
        /// Initializes a new instance of the <see cref="MovieSeriesService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="lookup">Series of the movies from TMDB.</param>
        /// <param name="worlds">Worlds (accessible items, world collections).</param>
        /// <param name="store">Stored recommendations (for the taste order).</param>
        public MovieSeriesService(
            ILogger<MovieSeriesService> logger,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            MovieSeriesLookup lookup,
            WorldService worlds,
            RecommendationStore store)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
            _worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Finds the part to continue with: the first unwatched part after the last watched one, otherwise the first unwatched part.
        /// </summary>
        /// <param name="played">Watched state of the parts in release order.</param>
        /// <returns>Index of the part, or -1 if all are watched.</returns>
        public static int NextIndex(IReadOnlyList<bool> played)
        {
            ArgumentNullException.ThrowIfNull(played);
            var last = -1;
            for (var i = 0; i < played.Count; i++)
            {
                if (played[i])
                {
                    last = i;
                }
            }

            for (var i = last + 1; i < played.Count; i++)
            {
                if (!played[i])
                {
                    return i;
                }
            }

            for (var i = 0; i < played.Count; i++)
            {
                if (!played[i])
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Orders the series: started ones first (most recently watched first), then the rest by taste; series that form a world last in each group.
        /// Finished series (all parts watched) are left out.
        /// </summary>
        /// <param name="series">The series.</param>
        /// <returns>The order.</returns>
        public static List<MovieSeries> Order(IEnumerable<MovieSeries> series)
        {
            ArgumentNullException.ThrowIfNull(series);
            return series
                .Where(s => s.NextIndex >= 0)
                .OrderBy(s => s.Started ? 0 : 1)
                .ThenBy(s => s.InWorld ? 1 : 0)
                .ThenByDescending(s => s.Started ? s.LastPlayed.Ticks : 0)
                .ThenByDescending(s => s.Taste)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Gets the series for a user (cached for some minutes).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>The series in display order.</returns>
        public IReadOnlyList<MovieSeries> GetSeries(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            var access = WorldService.AccessFingerprint(user);
            if (_userCache.TryGetValue(user.Id, out var cached) && cached.Access == access && DateTime.UtcNow - cached.Created < UserCacheDuration)
            {
                return cached.Series;
            }

            var result = Build(user);
            if (_userCache.Count > 200)
            {
                _userCache.Clear();
            }

            _userCache[user.Id] = (DateTime.UtcNow, access, result);
            return result;
        }

        /// <summary>
        /// Gets the series a movie belongs to (with all parts the user can see), or null.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="itemId">The movie.</param>
        /// <returns>The series, also finished ones, or null.</returns>
        public MovieSeries? GetSeriesOf(User user, Guid itemId)
        {
            ArgumentNullException.ThrowIfNull(user);
            var members = Membership();
            var entry = members.FirstOrDefault(m => m.Value.Movies.Contains(itemId));
            return entry.Key == null ? null : BuildOne(user, entry.Key, entry.Value.Name, entry.Value.Movies, WorldCollections(), TastePositions(user), null);
        }

        /// <summary>
        /// Gets a series by its ID (with all parts the user can see), or null.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="seriesId">The series ID.</param>
        /// <returns>The series, also finished ones, or null.</returns>
        public MovieSeries? GetSeriesById(User user, string seriesId)
        {
            ArgumentNullException.ThrowIfNull(user);
            return Membership().TryGetValue(seriesId, out var entry)
                ? BuildOne(user, seriesId, entry.Name, entry.Movies, WorldCollections(), TastePositions(user), null)
                : null;
        }

        /// <summary>
        /// Gets the series with a new part coming to cinemas soon (cinema start in the server's country where known), of which
        /// the user has at least one part: „Vor dem Kinostart: Hol die Reihe nach“. Soonest start first.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="today">The local day.</param>
        /// <param name="days">Days ahead.</param>
        /// <returns>The series with the upcoming part.</returns>
        public IReadOnlyList<(MovieSeries Series, string UpcomingTitle, DateOnly UpcomingDate)> GetUpcoming(User user, DateOnly today, int days)
        {
            ArgumentNullException.ThrowIfNull(user);
            var result = new List<(MovieSeries Series, string UpcomingTitle, DateOnly UpcomingDate)>();
            try
            {
                var members = Membership();
                var owned = _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true, IsVirtualItem = false })
                    .Select(m => m.GetProviderId(MetadataProvider.Tmdb))
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal);
                var worldCollections = WorldCollections();
                var taste = TastePositions(user);
                foreach (var (id, entry) in members)
                {
                    var upcoming = (_lookup.GetParts(id) ?? Array.Empty<MovieSeriesLookup.CollectionPart>())
                        .Where(p => !owned.Contains(p.TmdbId))
                        .Select(p => (Part: p, Date: DateOnly.TryParse(p.RegionDate ?? p.ReleaseDate, System.Globalization.CultureInfo.InvariantCulture, out var date) ? date : (DateOnly?)null))
                        .Where(p => p.Date > today && p.Date <= today.AddDays(days))
                        .OrderBy(p => p.Date)
                        .FirstOrDefault();
                    if (upcoming.Part == null)
                    {
                        continue;
                    }

                    var series = BuildOne(user, id, entry.Name, entry.Movies, worldCollections, taste, null, 1);
                    if (series != null)
                    {
                        result.Add((series, upcoming.Part.Title, upcoming.Date!.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kinostarts der Filmreihen konnten nicht ausgewertet werden");
            }

            return result.OrderBy(r => r.UpcomingDate).ToList();
        }

        /// <summary>
        /// Gets the IDs of all series in the library (also with only one part), for the nightly lookup of their parts.
        /// </summary>
        /// <returns>TMDB collection IDs.</returns>
        public IReadOnlyList<string> AllSeriesIds() => Membership().Keys.ToList();

        /// <summary>
        /// Forgets the cached series of all users (after a new lookup).
        /// </summary>
        public void Invalidate()
        {
            _userCache.Clear();
            lock (_membershipLock)
            {
                _membership = null;
            }
        }

        private static DateTime ReleaseDate(BaseItem item) =>
            item.PremiereDate ?? (item.ProductionYear is int year ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.MaxValue);

        private HashSet<string> WorldCollections()
        {
            var membership = _worlds.GetMembership();
            return membership.TmdbCollections.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);
        }

        private List<MovieSeries> Build(User user)
        {
            try
            {
                var worldCollections = WorldCollections();
                var taste = TastePositions(user);
                var genres = TopGenres(user);
                var result = new List<MovieSeries>();
                foreach (var (id, entry) in Membership())
                {
                    var series = BuildOne(user, id, entry.Name, entry.Movies, worldCollections, taste, genres);
                    if (series != null)
                    {
                        result.Add(series);
                    }
                }

                return Order(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filmreihen konnten nicht ausgewertet werden");
                return new List<MovieSeries>();
            }
        }

        private MovieSeries? BuildOne(User user, string id, string name, List<Guid> movieIds, HashSet<string> worldCollections, Dictionary<Guid, double> taste, Dictionary<string, double>? genres, int minParts = MinParts)
        {
            var accessible = _worlds.AccessibleItems(user);
            var parts = movieIds
                .Select(m => accessible.TryGetValue(m, out var item) ? item : null)
                .OfType<BaseItem>()
                .GroupBy(RecommendationSectionHandler.DuplicateKey)
                .Select(g => g.First())
                .OrderBy(ReleaseDate)
                .ThenBy(p => p.SortName ?? p.Name, StringComparer.Ordinal)
                .ToList();
            if (parts.Count < minParts)
            {
                return null;
            }

            var lookup = UserDataLookup.Load(_userDataManager, user, parts);
            var data = parts.Select(p => lookup.Get(p)).ToList();
            var played = data.Select(d => d?.Played == true).ToList();
            var next = NextIndex(played);
            var started = played.Any(p => p) || data.Any(d => d?.PlaybackPositionTicks > 0);
            var tasteScore = parts.Select(p => taste.GetValueOrDefault(p.Id)).DefaultIfEmpty(0).Max();
            if (tasteScore == 0 && genres != null && genres.Count > 0)
            {
                // Ohne Empfehlung aus der Reihe: Nähe der Genres zu den meistgesehenen Genres, schwächer gewichtet
                var seriesGenres = parts.SelectMany(p => p.Genres ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                tasteScore = 0.5 * seriesGenres.Select(g => genres.GetValueOrDefault(g)).DefaultIfEmpty(0).Max();
            }

            // Teil einer Welt: Sammlung der Welt zugeordnet oder alle vorhandenen Teile gehören zur selben Welt
            return new MovieSeries
            {
                Id = id,
                Name = name,
                Parts = parts,
                Played = played,
                NextIndex = next,
                Started = started,
                InWorld = worldCollections.Contains(id) || _worlds.GetWorlds().Members.Values.Any(set => parts.All(part => set.Contains(part.Id))),
                LastPlayed = data.Where(d => d?.Played == true).Select(d => d!.LastPlayedDate ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max(),
                Taste = tasteScore
            };
        }

        // Stelle der Teile in den gespeicherten Film-Empfehlungen: 1 für die erste, fallend
        private Dictionary<Guid, double> TastePositions(User user)
        {
            var movies = _store.Get(user.Id)?.Movies ?? Array.Empty<Guid>();
            var count = Math.Max(1, movies.Count);
            return movies.Select((id, index) => (id, Score: 1.0 - ((double)index / count))).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.Max(x => x.Score));
        }

        // Anteil der Genres an den zuletzt gesehenen Filmen (0..1, das häufigste Genre = 1)
        private Dictionary<string, double> TopGenres(User user)
        {
            var played = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = 300
            });
            var counts = played.SelectMany(p => p.Genres ?? Array.Empty<string>())
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var max = counts.Count == 0 ? 1 : counts.Values.Max();
            return counts.ToDictionary(e => e.Key, e => (double)e.Value / max, StringComparer.OrdinalIgnoreCase);
        }

        // Alle Reihen im Bestand: TMDB-Sammlung am Film, Jellyfin-Sammlungen mit TMDB-ID, Abfrage bei TMDB
        private Dictionary<string, (string Name, List<Guid> Movies)> Membership()
        {
            var version = _lookup.Version;
            lock (_membershipLock)
            {
                if (_membership is { } cached && cached.Version == version && DateTime.UtcNow - cached.Created < MembershipCacheDuration)
                {
                    return cached.Members;
                }
            }

            var members = new Dictionary<string, (string Name, List<Guid> Movies)>(StringComparer.Ordinal);
            void Add(string seriesId, string? name, Guid movieId)
            {
                if (!members.TryGetValue(seriesId, out var entry))
                {
                    entry = (string.Empty, new List<Guid>());
                }

                if (string.IsNullOrEmpty(entry.Name) && !string.IsNullOrWhiteSpace(name))
                {
                    entry.Name = name;
                }

                if (!entry.Movies.Contains(movieId))
                {
                    entry.Movies.Add(movieId);
                }

                members[seriesId] = entry;
            }

            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false
            });
            foreach (var movie in movies)
            {
                var seriesId = movie.GetProviderId("TmdbCollection");
                if (string.IsNullOrEmpty(seriesId))
                {
                    seriesId = _lookup.SeriesOf(movie.GetProviderId(MetadataProvider.Tmdb));
                }

                if (!string.IsNullOrEmpty(seriesId))
                {
                    Add(seriesId, _lookup.GetSeries(seriesId)?.Name, movie.Id);
                }
            }

            foreach (var boxSet in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.BoxSet } }).OfType<BoxSet>())
            {
                // Nur Sammlungen mit TMDB-ID sind Filmreihen – eigene Zusammenstellungen nicht
                var seriesId = boxSet.GetProviderId(MetadataProvider.Tmdb);
                if (string.IsNullOrEmpty(seriesId))
                {
                    continue;
                }

                foreach (var movie in boxSet.GetLinkedChildren().OfType<Movie>())
                {
                    Add(seriesId, _lookup.GetSeries(seriesId)?.Name ?? boxSet.Name, movie.Id);
                }
            }

            foreach (var seriesId in members.Keys.Where(k => string.IsNullOrEmpty(members[k].Name)).ToList())
            {
                var entry = members[seriesId];
                entry.Name = "Filmreihe";
                members[seriesId] = entry;
            }

            // Auch Reihen mit nur einem Teil (für „Vor dem Kinostart“); die Zeile verlangt je Benutzer mindestens zwei
            lock (_membershipLock)
            {
                _membership = (DateTime.UtcNow, version, members);
            }

            return members;
        }

        /// <summary>
        /// A movie series in the libraries of a user.
        /// </summary>
        public class MovieSeries
        {
            /// <summary>Gets or sets the TMDB collection ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the parts the user can see, in release order.</summary>
            public IReadOnlyList<BaseItem> Parts { get; set; } = Array.Empty<BaseItem>();

            /// <summary>Gets or sets the watched state per part.</summary>
            public IReadOnlyList<bool> Played { get; set; } = Array.Empty<bool>();

            /// <summary>Gets or sets the index of the part to watch next (-1 = all watched).</summary>
            public int NextIndex { get; set; } = -1;

            /// <summary>Gets or sets a value indicating whether the user started the series.</summary>
            public bool Started { get; set; }

            /// <summary>Gets or sets a value indicating whether the series belongs to a world.</summary>
            public bool InWorld { get; set; }

            /// <summary>Gets or sets when a part was last watched.</summary>
            public DateTime LastPlayed { get; set; }

            /// <summary>Gets or sets how well it fits the taste (0–1).</summary>
            public double Taste { get; set; }

            /// <summary>Gets the number of watched parts.</summary>
            public int WatchedCount => Played.Count(p => p);

            /// <summary>Gets the reason line: „Weiter mit Teil 3“ or „4 Teile“.</summary>
            public string Reason => Started && NextIndex >= 0 ? "Weiter mit Teil " + (NextIndex + 1) : Parts.Count + " Teile";
        }
    }
}
