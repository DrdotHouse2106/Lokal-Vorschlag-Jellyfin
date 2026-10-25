using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Verhalten über den Sehverlauf hinaus – alles lokal auf dem Server:
    /// <list type="bullet">
    /// <item>Tageszeit: was jemand morgens, tagsüber, abends, nachts schaut (Reihenfolge der Zeilen, typische Genres).</item>
    /// <item>Kurzfristiges Interesse: Suchen und angesehene Titel der letzten Tage.</item>
    /// </list>
    /// Beides sortiert die gespeicherten Empfehlungen beim Abruf leicht um.
    /// </summary>
    public class BehaviorService
    {
        private const int MaxMovies = 500;
        private const int MaxEpisodes = 800;
        private static readonly TimeSpan ProfileCacheDuration = TimeSpan.FromHours(1);
        private static readonly TimeSpan InterestCacheDuration = TimeSpan.FromMinutes(10);

        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly BehaviorStore _store;
        private readonly LiveTvWatchStore? _liveTv;
        private readonly ILogger<BehaviorService> _logger;
        private readonly ConcurrentDictionary<Guid, (DateTime Created, DaypartProfile Profile)> _profiles = new();
        private readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<Guid, string>> _reasons = new();
        private readonly ConcurrentDictionary<Guid, (DateTime Created, long Version, List<Interest> Interests)> _interests = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="BehaviorService"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="store">Searches and viewed titles.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="liveTv">Live TV watch history, optional.</param>
        public BehaviorService(ILibraryManager libraryManager, IUserDataManager userDataManager, BehaviorStore store, ILogger<BehaviorService> logger, LiveTvWatchStore? liveTv = null)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _liveTv = liveTv;
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the UTC offset to use: the one of the app (browser), otherwise the server's.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset sent by the app, optional.</param>
        /// <returns>Offset in minutes (e.g. 120 for CEST).</returns>
        public static int ResolveOffset(int? utcOffsetMinutes)
        {
            return utcOffsetMinutes is >= -840 and <= 840
                ? utcOffsetMinutes.Value
                : (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
        }

        /// <summary>
        /// Gets the time-of-day profile of a user (cached for an hour).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>The profile.</returns>
        public DaypartProfile GetProfile(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (_profiles.TryGetValue(user.Id, out var cached) && DateTime.UtcNow - cached.Created < ProfileCacheDuration)
            {
                return cached.Profile;
            }

            var profile = new DaypartProfile();
            try
            {
                foreach (var movie in Played(user, BaseItemKind.Movie, MaxMovies))
                {
                    if (_userDataManager.GetUserData(user, movie)?.LastPlayedDate is { } played)
                    {
                        profile.Add(played, DaypartProfile.Movies, movie.Genres);
                        profile.Rhythm.Add(played, RuntimeMinutes(movie));
                    }
                }

                foreach (var episode in Played(user, BaseItemKind.Episode, MaxEpisodes).OfType<Episode>())
                {
                    if (_userDataManager.GetUserData(user, episode)?.LastPlayedDate is { } played)
                    {
                        var genres = episode.Series?.Genres is { Length: > 0 } seriesGenres ? seriesGenres : episode.Genres;
                        profile.Add(played, DaypartProfile.Series, genres);
                        profile.Rhythm.Add(played, RuntimeMinutes(episode));
                    }
                }

                foreach (var entry in _liveTv?.GetEntries(user.Id, Config.LiveTvMinMinutes) ?? Array.Empty<LiveTvWatchEntry>())
                {
                    profile.Add(entry.LastWatched, DaypartProfile.LiveTv, entry.Genres);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                _logger.LogDebug(ex, "Tageszeit-Profil von {UserId} unvollständig", user.Id);
            }

            _profiles[user.Id] = (DateTime.UtcNow, profile);
            return profile;
        }

        /// <summary>
        /// Gets the order of the recommendation rows for now (movies, series, live TV), most typical for the time of day first.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone, optional.</param>
        /// <returns>Daypart, its name and the order.</returns>
        public (int Daypart, string Label, IReadOnlyList<string> Order, int Plays) RowOrder(User user, int? utcOffsetMinutes)
        {
            ArgumentNullException.ThrowIfNull(user);
            var offset = ResolveOffset(utcOffsetMinutes);
            var daypart = DaypartProfile.DaypartAt(DateTime.UtcNow, offset);
            var profile = GetProfile(user);
            var order = Config.EnableDaypart ? profile.KindOrder(daypart, offset) : new[] { DaypartProfile.Movies, DaypartProfile.Series, DaypartProfile.LiveTv };
            return (daypart, DaypartProfile.Label(daypart), order, profile.PlaysIn(daypart, offset));
        }

        /// <summary>
        /// Resolves a search term to library titles the user may see: titles by name, otherwise titles with a person
        /// of that name, otherwise titles of the genre.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="term">Search term.</param>
        /// <returns>Up to 10 titles.</returns>
        public IReadOnlyList<Guid> ResolveSearch(User user, string term) => ResolveSearchWithKind(user, term).Matches;

        /// <summary>
        /// Resolves a search term like <see cref="ResolveSearch"/> and tells how the titles were found. Titles found by name only
        /// count if the name really contains the term („the“ alone finds nothing).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="term">Search term.</param>
        /// <returns>Up to 10 titles and the kind („Title“, „Person“, „Genre“).</returns>
        public (IReadOnlyList<Guid> Matches, string Kind) ResolveSearchWithKind(User user, string term)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(term);
            if (!SearchTerms.IsMeaningful(term))
            {
                return (Array.Empty<Guid>(), BehaviorStore.SearchKindTitle);
            }

            var kinds = new[] { BaseItemKind.Movie, BaseItemKind.Series };
            var byName = _libraryManager.GetItemList(new InternalItemsQuery(user) { SearchTerm = term, IncludeItemTypes = kinds, Recursive = true, Limit = 20 })
                .Where(i => SearchTerms.TitleFits(term, i.Name) || SearchTerms.TitleFits(term, i.OriginalTitle))
                .Take(10)
                .ToList();
            if (byName.Count > 0)
            {
                return (byName.Select(i => i.Id).ToList(), BehaviorStore.SearchKindTitle);
            }

            var person = _libraryManager.GetItemList(new InternalItemsQuery { SearchTerm = term, IncludeItemTypes = new[] { BaseItemKind.Person }, Limit = 1 }).FirstOrDefault();
            if (person != null)
            {
                var withPerson = _libraryManager.GetItemList(new InternalItemsQuery(user) { PersonIds = new[] { person.Id }, IncludeItemTypes = kinds, Recursive = true, Limit = 10 });
                if (withPerson.Count > 0)
                {
                    return (withPerson.Select(i => i.Id).ToList(), BehaviorStore.SearchKindPerson);
                }
            }

            return (_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                Genres = new[] { term },
                IncludeItemTypes = kinds,
                Recursive = true,
                Limit = 10,
                OrderBy = new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) }
            }).Select(i => i.Id).ToList(), BehaviorStore.SearchKindGenre);
        }

        /// <summary>
        /// Sorts the stored recommendations of a user again for now: titles similar to recent searches or viewed titles
        /// and titles with genres typical for the time of day move up. Notes the reasons for <see cref="GetReasons"/>.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="items">Recommendations in stored order.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone, optional.</param>
        /// <returns>The new order.</returns>
        public IReadOnlyList<BaseItem> Rerank(User user, IReadOnlyList<BaseItem> items, int? utcOffsetMinutes)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(items);
            var config = Config;
            if (items.Count < 2)
            {
                return items;
            }

            var offset = ResolveOffset(utcOffsetMinutes);
            var now = DateTime.UtcNow;
            var daypart = DaypartProfile.DaypartAt(now, offset);
            var daypartOn = config.EnableDaypart && config.DaypartWeight > 0;
            var rhythmOn = config.EnableWatchRhythm && config.WatchRhythmWeight > 0;
            var profile = daypartOn || rhythmOn ? GetProfile(user) : null;
            var priorOnly = profile != null && profile.PlaysIn(daypart, offset) < DaypartProfile.MinPlays;
            var local = now.AddMinutes(offset);
            var remaining = rhythmOn ? profile!.Rhythm.RemainingMinutes(local, offset) : null;
            var preferShort = rhythmOn && profile!.Rhythm.PreferShort(local, offset);
            var interests = config.EnableBehaviorSignals && config.InterestWeight > 0 && _store.IsEnabled(user.Id) ? Interests(user, now) : new List<Interest>();

            var reasons = new Dictionary<Guid, string>();
            var reranked = InterestScoring.Rerank(items, item =>
            {
                var bonus = !daypartOn || profile == null ? 0
                    : config.DaypartWeight * (priorOnly ? DaypartProfile.PriorGenreScore(item.Genres, daypart) : profile.GenreScore(item.Genres, daypart, offset));
                if (rhythmOn)
                {
                    // Spät abends und unter der Woche: was noch in den Abend passt, rückt vor
                    bonus -= config.WatchRhythmWeight * (1 - WatchRhythm.RuntimeFactor(RuntimeMinutes(item), remaining, preferShort));
                }

                if (interests.Count > 0)
                {
                    var features = Features(item);
                    var best = interests
                        .Select(i => (Interest: i, Score: i.Weight * (i.ItemId == item.Id ? 1 : InterestScoring.Similarity(features, i.Features))))
                        .OrderByDescending(x => x.Score)
                        .First();
                    var interestBonus = config.InterestWeight * best.Score;
                    bonus += interestBonus;

                    // Der Suchgrund gilt nur für den gefundenen Titel selbst, nicht für ähnliche; ohne passenden Grund bleibt der bisherige
                    var reason = best.Interest.Reason;
                    var searchReason = reason.StartsWith("Weil du nach", StringComparison.Ordinal);
                    if (interestBonus >= 0.12 && reason.Length > 0 && (!searchReason || best.Interest.ItemId == item.Id))
                    {
                        reasons[item.Id] = reason;
                    }
                }

                return bonus;
            });

            var merged = new Dictionary<Guid, string>(_reasons.GetValueOrDefault(user.Id) ?? new Dictionary<Guid, string>());
            foreach (var item in items)
            {
                merged.Remove(item.Id);
            }

            foreach (var (id, reason) in reasons)
            {
                merged[id] = reason;
            }

            _reasons[user.Id] = merged;
            return reranked;
        }

        /// <summary>
        /// Gets the reasons from recent searches and viewed titles found during the last <see cref="Rerank"/> of a user.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>Reasons by item ID.</returns>
        public IReadOnlyDictionary<Guid, string> GetReasons(Guid userId) => _reasons.GetValueOrDefault(userId) ?? new Dictionary<Guid, string>();

        /// <summary>
        /// Forgets the cached state of a user (after deleting the stored behavior).
        /// </summary>
        /// <param name="userId">The user.</param>
        public void Forget(Guid userId)
        {
            _reasons.TryRemove(userId, out _);
            _interests.TryRemove(userId, out _);
        }

        // Laufzeit in Minuten; bei Serien die übliche Folgenlänge
        private static double? RuntimeMinutes(BaseItem item) => item.RunTimeTicks is long ticks && ticks > 0 ? ticks / (double)TimeSpan.TicksPerMinute : null;

        private static IReadOnlyCollection<string> Features(BaseItem item) => (item.Genres ?? Array.Empty<string>()).Concat(item.Tags ?? Array.Empty<string>()).ToList();

        private IReadOnlyList<BaseItem> Played(User user, BaseItemKind kind, int limit)
        {
            return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { kind },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = limit
            });
        }

        // Alle gespeicherten Suchen und angesehenen Titel, älter = schwächer; mehrfaches Interesse am selben Titel addiert sich.
        // Zwischengespeichert, bis sich die Einträge ändern (höchstens 10 Minuten, damit das Altern wirkt).
        private List<Interest> Interests(User user, DateTime now)
        {
            var version = _store.Version;
            if (_interests.TryGetValue(user.Id, out var cached) && cached.Version == version && now - cached.Created < InterestCacheDuration)
            {
                return cached.Interests;
            }

            var behavior = _store.Get(user.Id);
            var decayDays = Math.Clamp(Config.InterestDecayDays, 3, 365);

            // Nur abgeschlossene Suchen (ein Treffer wurde geöffnet oder abgespielt) mit echtem Suchbegriff
            var entries = new List<(Guid ItemId, double Weight, string Reason, string? Term, string? Kind)>();
            foreach (var search in behavior.Searches.Where(s => s.Confirmed && SearchTerms.IsMeaningful(s.Term)))
            {
                var weight = InterestScoring.SearchWeight * InterestScoring.Decay(now - search.At, decayDays);
                if (weight >= InterestScoring.MinWeight)
                {
                    entries.AddRange(search.Matches.Take(5).Select(id => (id, weight, "Weil du nach „" + search.Term + "“ gesucht hast", (string?)search.Term, (string?)search.Kind)));
                }
            }

            foreach (var view in behavior.Views)
            {
                var weight = InterestScoring.ViewWeight * InterestScoring.Decay(now - view.At, decayDays);
                if (weight >= InterestScoring.MinWeight)
                {
                    entries.Add((view.ItemId, weight, string.Empty, null, null));
                }
            }

            var result = new List<Interest>();
            foreach (var group in entries.GroupBy(e => e.ItemId).OrderByDescending(g => InterestScoring.Combine(g.Select(e => e.Weight))).Take(InterestScoring.MaxInterests))
            {
                if (_libraryManager.GetItemById(group.Key) is not { } item || !item.IsVisibleStandalone(user))
                {
                    continue;
                }

                // Grund „Weil du nach … gesucht hast“ nur, wenn der Titel wirklich zum Begriff passt (bei Personen und Genres immer)
                var fitting = group
                    .Where(e => e.Term == null || e.Kind != BehaviorStore.SearchKindTitle || SearchTerms.TitleFits(e.Term, item.Name) || SearchTerms.TitleFits(e.Term, item.OriginalTitle))
                    .OrderByDescending(e => e.Weight)
                    .FirstOrDefault();
                var reason = fitting.Reason is null ? string.Empty
                    : fitting.Reason.Length > 0 ? fitting.Reason
                    : "Weil du dir „" + item.Name + "“ angesehen hast";
                result.Add(new Interest(item.Id, Features(item), InterestScoring.Combine(group.Select(e => e.Weight)), reason));
            }

            _interests[user.Id] = (now, version, result);
            return result;
        }

        private sealed record Interest(Guid ItemId, IReadOnlyCollection<string> Features, double Weight, string Reason);
    }
}
