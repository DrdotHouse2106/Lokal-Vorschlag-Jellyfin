using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Sucht im EPG die Sendungen der nächsten Stunden heraus, die zum Geschmack eines Benutzers passen
    /// („Heute im TV für dich“).
    /// </summary>
    public class LiveTvRecommendationService
    {
        private const float MinScore = 0.15f;
        private const int MaxLibraryMovies = 300;
        private const int MaxLibraryEpisodes = 500;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        private readonly ILogger<LiveTvRecommendationService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly ILiveTvManager _liveTvManager;
        private readonly LiveTvWatchStore _watchStore;
        private readonly FeedbackStore _feedback;
        private readonly ConcurrentDictionary<Guid, (DateTime Created, IReadOnlyList<BaseItem> Items)> _cache =
            new ConcurrentDictionary<Guid, (DateTime Created, IReadOnlyList<BaseItem> Items)>();

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvRecommendationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="liveTvManager">Live TV manager.</param>
        /// <param name="watchStore">Live TV watch history.</param>
        /// <param name="feedback">Evaluation of the recommendations.</param>
        public LiveTvRecommendationService(
            ILogger<LiveTvRecommendationService> logger,
            ILibraryManager libraryManager,
            IUserManager userManager,
            ILiveTvManager liveTvManager,
            LiveTvWatchStore watchStore,
            FeedbackStore feedback)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _liveTvManager = liveTvManager ?? throw new ArgumentNullException(nameof(liveTvManager));
            _watchStore = watchStore ?? throw new ArgumentNullException(nameof(watchStore));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        }

        /// <summary>
        /// Selects and orders upcoming programs for a user.
        /// </summary>
        /// <param name="profile">Interest profile of the user.</param>
        /// <param name="programs">Candidate programs.</param>
        /// <param name="excludedGenres">Excluded genres (normalized).</param>
        /// <param name="watchedBroadcasts">Broadcast keys the user already watched.</param>
        /// <param name="limit">Maximum number of programs.</param>
        /// <param name="onlyRegularShowsAfter">Programs starting after this time (UTC) are only kept if they are regular shows.</param>
        /// <returns>The recommended programs, in chronological order.</returns>
        public static IReadOnlyList<ProgramCandidate> Rank(
            TvInterestProfile profile,
            IEnumerable<ProgramCandidate> programs,
            HashSet<string> excludedGenres,
            HashSet<string> watchedBroadcasts,
            int limit,
            DateTime? onlyRegularShowsAfter = null)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(programs);

            if (profile.IsEmpty)
            {
                return Array.Empty<ProgramCandidate>();
            }

            var ranked = programs
                .Where(p => !LiveTvText.IsExcluded(p.Genres, excludedGenres) && !watchedBroadcasts.Contains(p.BroadcastKey))

                // Stammsendungen dürfen weiter in der Zukunft liegen als der normale Vorschau-Zeitraum
                .Where(p => onlyRegularShowsAfter == null || p.StartDate <= onlyRegularShowsAfter || profile.IsRegular(p.TitleKey))

                // Sport nur bei gleicher Sportart oder gleichem Titel: wer Fußball schaut, bekommt kein Handball, Golf oder Tennis
                .Where(p => !p.IsSports || profile.HasSport(p.Sport) || profile.HasTitle(p.TitleKey))
                .Select(p => (Program: p, Score: profile.Score(p.Genres, p.TitleKey, p.People, p.Sport)))
                .Where(x => x.Score >= MinScore)
                .GroupBy(x => DedupeKey(x.Program))
                .Select(g => g.OrderBy(x => x.Program.StartDate).First()) // pro Sendung nur der nächste Termin
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Program.StartDate)
                .Select(x => x.Program)
                .ToList();

            // Die besten auswählen, dann in der Zeile chronologisch anzeigen
            return PickDiverse(ranked, limit, profile).OrderBy(p => p.StartDate).ToList();
        }

        /// <summary>
        /// Key for the evaluation: sports per broadcast (pairing), everything else per show.
        /// </summary>
        /// <param name="program">Recommended program.</param>
        /// <returns>The key.</returns>
        public static string FeedbackKey(ProgramCandidate program)
        {
            ArgumentNullException.ThrowIfNull(program);
            return program.IsSports || program.TitleKey.Length == 0 ? program.BroadcastKey : "title:" + program.TitleKey;
        }

        /// <summary>
        /// Gets the recommended upcoming programs for a user (cached for a few minutes).
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <returns>Programs (<see cref="LiveTvProgram"/>) in recommendation order.</returns>
        public IReadOnlyList<BaseItem> GetRecommendedPrograms(Guid userId)
        {
            var now = DateTime.UtcNow;
            if (_cache.TryGetValue(userId, out var cached) && now - cached.Created < CacheDuration)
            {
                return cached.Items;
            }

            var items = ComputeRecommendedPrograms(userId, now);
            _cache[userId] = (now, items);
            return items;
        }

        /// <summary>
        /// Picks the best programs, but at most a third of the row from the same main genre
        /// (e.g. not 17 comedies). If there are not enough other programs the row is filled up.
        /// </summary>
        /// <param name="ranked">Programs ordered by score.</param>
        /// <param name="limit">Maximum number of programs.</param>
        /// <param name="profile">Interest profile (regular shows are always included).</param>
        /// <returns>The selected programs.</returns>
        private static List<ProgramCandidate> PickDiverse(List<ProgramCandidate> ranked, int limit, TvInterestProfile profile)
        {
            var max = limit > 0 ? limit : int.MaxValue;
            var cap = limit > 0 ? Math.Max(2, (int)Math.Ceiling(limit / 3.0)) : int.MaxValue;
            var perGenre = new Dictionary<string, int>();
            var picked = new List<ProgramCandidate>();
            var leftovers = new List<ProgramCandidate>();

            // Stammsendungen immer zuerst und ohne Genre-Obergrenze
            picked.AddRange(ranked.Where(p => profile.IsRegular(p.TitleKey)).Take(max));

            foreach (var program in ranked.Where(p => !profile.IsRegular(p.TitleKey)))
            {
                if (picked.Count >= max)
                {
                    break;
                }

                var genre = program.Sport ?? LiveTvText.NormalizeFeature(program.Genres.FirstOrDefault());
                var count = perGenre.GetValueOrDefault(genre);
                if (count < cap)
                {
                    perGenre[genre] = count + 1;
                    picked.Add(program);
                }
                else
                {
                    leftovers.Add(program);
                }
            }

            picked.AddRange(leftovers.Take(max - picked.Count));
            return picked;
        }

        /// <summary>
        /// Key used to show each show only once. Sports broadcasts are distinguished by their pairing
        /// (episode title), so several football games can be recommended at the same time.
        /// </summary>
        private static string DedupeKey(ProgramCandidate program)
        {
            if (program.IsSports)
            {
                var key = LiveTvText.NormalizeFeature(program.Title) + "|" + LiveTvText.NormalizeFeature(program.EpisodeTitle);
                if (key.Length > 1)
                {
                    return key;
                }
            }

            return program.TitleKey.Length > 0 ? program.TitleKey : program.BroadcastKey;
        }

        private IReadOnlyList<BaseItem> ComputeRecommendedPrograms(Guid userId, DateTime now)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var user = _userManager.GetUserById(userId);
            if (user == null || !config.EnableLiveTvSection || !user.HasPermission(PermissionKind.EnableLiveTvAccess))
            {
                return Array.Empty<BaseItem>();
            }

            var excluded = LiveTvText.ParseList(config.LiveTvExcludedGenres);
            var watchedEntries = config.EnableLiveTvTracking
                ? _watchStore.GetEntries(userId, config.LiveTvMinMinutes)
                : Array.Empty<LiveTvWatchEntry>();

            var profile = BuildProfile(user, watchedEntries, excluded, config, now);
            var lookaheadEnd = now.AddHours(Math.Max(1, config.LiveTvLookaheadHours));
            if (profile.IsEmpty)
            {
                return Array.Empty<BaseItem>();
            }

            var liveTvFolder = _liveTvManager.GetInternalLiveTvFolder(CancellationToken.None);
            var programs = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.LiveTvProgram },
                MinEndDate = now,
                MaxStartDate = profile.HasRegularShows ? now.AddDays(Math.Max(7, Math.Max(1, config.LiveTvLookaheadHours) / 24.0)) : lookaheadEnd,
                TopParentIds = new[] { liveTvFolder.Id },
                OrderBy = new[] { (ItemSortBy.StartDate, SortOrder.Ascending) }
            }).OfType<LiveTvProgram>().ToList();

            var candidates = programs.Select(p => new ProgramCandidate
            {
                Item = p,
                BroadcastKey = p.ChannelId.ToString("N") + "|" + p.StartDate.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
                TitleKey = LiveTvText.NormalizeTitle(string.IsNullOrWhiteSpace(p.SeriesName) ? p.Name : p.SeriesName),
                Genres = p.Genres ?? Array.Empty<string>(),
                People = Array.Empty<string>(),
                StartDate = p.StartDate,
                Title = p.Name ?? string.Empty,
                EpisodeTitle = p.EpisodeTitle,
                Sport = LiveTvText.DetectSport(p.Genres, p.Name, p.EpisodeTitle, p.SeriesName)
            }).Select(c =>
            {
                var taggedAsSports = ((LiveTvProgram)c.Item!).IsSports;
                if (c.Sport == null && taggedAsSports)
                {
                    // Als Sport markiert, aber Genre ohne Sportart: Sportart aus dem Titel bestimmen
                    c.Sport = LiveTvText.DetectSport(null, c.Title, c.EpisodeTitle);
                }

                c.IsSports = c.Sport != null || LiveTvText.IsSportsGenre(c.Genres) || taggedAsSports;
                return c;
            });

            var watchedKeys = watchedEntries.Select(e => e.BroadcastKey).ToHashSet();
            var ranked = Rank(profile, candidates, excluded, watchedKeys, config.LiveTvRecommendationCount, lookaheadEnd);
            _feedback.RecordRecommended(userId, FeedbackStore.LiveTv, ranked.Select(c => (FeedbackKey(c), LiveTvDisplay.Title(c.Title, c.EpisodeTitle, c.IsSports))), now);

            _logger.LogDebug(
                "Live TV recommendations for {User}: {Count} of {Total} programs",
                user.Username,
                ranked.Count,
                programs.Count);

            return ranked.Select(c => c.Item!).ToList();
        }

        private TvInterestProfile BuildProfile(
            User user,
            IReadOnlyList<LiveTvWatchEntry> watchedEntries,
            HashSet<string> excluded,
            PluginConfiguration config,
            DateTime now)
        {
            var profile = new TvInterestProfile();

            // 1. Live geschaut: Minuten zählen (max. 3 Stunden je Sendung), ältere Einträge zählen weniger
            foreach (var entry in watchedEntries.Where(e => !LiveTvText.IsExcluded(e.Genres, excluded)))
            {
                var hours = (float)Math.Min(entry.Minutes, 180) / 60f;
                var decay = WeightCalculator.ExponentialDecay((now - entry.LastWatched).TotalDays, config.RecencyDecayHalfLifeDays);
                var sport = LiveTvText.DetectSport(entry.Genres, entry.Title, entry.EpisodeTitle, entry.SeriesName);
                profile.Add(entry.Genres, LiveTvText.TitleKey(entry), entry.People, hours * decay, sport);
            }

            // Stammsendungen: an mindestens N verschiedenen Tagen geschaut (z. B. „Die Sendung mit der Maus“).
            // Sport ausgenommen – dort sorgt die Sportart-Erkennung für passende Vorschläge.
            var timeZone = LiveTvDisplay.ResolveTimeZone(config.LiveTvTimeZone);
            var regularShows = watchedEntries
                .Where(e => !LiveTvText.IsExcluded(e.Genres, excluded)
                    && LiveTvText.DetectSport(e.Genres, e.Title, e.EpisodeTitle, e.SeriesName) == null)
                .GroupBy(LiveTvText.TitleKey)
                .Where(g => g.Key.Length > 0
                    && g.Select(e => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.StartDate, DateTimeKind.Utc), timeZone).Date).Distinct().Count() >= Math.Max(2, config.LiveTvRegularDays));
            foreach (var show in regularShows)
            {
                profile.MarkRegular(show.Key);
            }

            // 2. Bibliothek: gesehene Filme
            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxLibraryMovies
            }).OfType<Movie>();

            foreach (var movie in movies)
            {
                profile.Add(movie.Genres, LiveTvText.NormalizeTitle(movie.Name), null, 0.5f);
            }

            // 3. Bibliothek: Serien mit gesehenen Folgen (neue Folgen im TV landen so weit vorne)
            var episodesBySeries = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxLibraryEpisodes
            }).OfType<Episode>().GroupBy(e => e.SeriesId);

            foreach (var group in episodesBySeries)
            {
                var series = _libraryManager.GetItemById(group.Key) as Series;
                var name = series?.Name ?? group.First().SeriesName;
                profile.Add(series?.Genres, LiveTvText.NormalizeTitle(name), null, Math.Min(group.Count(), 10) / 5f);
            }

            profile.Normalize();
            return profile;
        }

        /// <summary>
        /// A program considered for the recommendations.
        /// </summary>
        public class ProgramCandidate
        {
            /// <summary>
            /// Gets or sets the program item.
            /// </summary>
            public BaseItem? Item { get; set; }

            /// <summary>
            /// Gets or sets the broadcast key (channel + start).
            /// </summary>
            public string BroadcastKey { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the normalized title key.
            /// </summary>
            public string TitleKey { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the genres.
            /// </summary>
            public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

            /// <summary>
            /// Gets or sets the people.
            /// </summary>
            public IReadOnlyList<string> People { get; set; } = Array.Empty<string>();

            /// <summary>
            /// Gets or sets the start date (UTC).
            /// </summary>
            public DateTime StartDate { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the program is a sports broadcast.
            /// </summary>
            public bool IsSports { get; set; }

            /// <summary>
            /// Gets or sets the detected sport (e.g. "fußball"), if any.
            /// </summary>
            public string? Sport { get; set; }

            /// <summary>
            /// Gets or sets the program title.
            /// </summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the episode title (for sports usually the pairing).
            /// </summary>
            public string? EpisodeTitle { get; set; }
        }
    }
}
