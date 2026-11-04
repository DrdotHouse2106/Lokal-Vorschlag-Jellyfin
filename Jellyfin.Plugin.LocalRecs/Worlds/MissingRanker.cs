using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Discover;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Sortiert fehlende Titel einer Welt persönlich: Genres des Gesehenen (Favoriten und „Gefällt mir“ zählen mehr),
    /// gesehene Teile derselben Filmreihe, Suchbegriffe, dazu TMDB-Bewertung und -Beliebtheit; neuere Titel leicht vorn.
    /// Das Profil eines Benutzers wird 10 Minuten zwischengespeichert; die TMDB-Daten liegen global im DiscoverService.
    /// </summary>
    public class MissingRanker
    {
        private const int MaxPlayed = 300;
        private static readonly TimeSpan ProfileLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SearchWindow = TimeSpan.FromDays(60);

        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly BehaviorStore? _behavior;
        private readonly ConcurrentDictionary<Guid, (DateTime Created, TasteProfile Profile)> _profiles = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="MissingRanker"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="behavior">Search and view history, optional.</param>
        public MissingRanker(ILibraryManager libraryManager, IUserDataManager userDataManager, BehaviorStore? behavior = null)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _behavior = behavior;
        }

        /// <summary>
        /// Scores a title for a profile (0–1) and names the reason, e.g. „Weil du „Halloween“ gesehen hast“.
        /// </summary>
        /// <param name="item">The missing title.</param>
        /// <param name="profile">The user's taste.</param>
        /// <param name="today">Today.</param>
        /// <returns>Score and reason (or null).</returns>
        public static (double Score, string? Reason) Score(DiscoverItem item, TasteProfile profile, DateOnly today)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(profile);

            // Genres nach Gewicht ihrer Stelle: TMDB nennt das Hauptgenre zuerst (Horror vor Komödie)
            var weights = item.Genres.Select((g, i) => (profile.Genres.TryGetValue(g, out var w) ? w : 0) * GenrePosition(i)).ToList();
            var genre = weights.Count == 0 ? 0 : (0.7 * weights.Max()) + (0.3 * weights.Average());
            string? seenPart = null;
            var series = item.CollectionId is int collection && profile.Collections.TryGetValue(collection, out seenPart) ? 1.0 : 0.0;

            // Suchbegriffe nur als ganze Wörter bzw. Titelanfang; Treffer im deutschen Titel zählen mehr als im Originaltitel
            var searched = profile.SearchTerms.FirstOrDefault(t => Fits(t, item.Title));
            var searchWeight = searched != null ? 0.20 : 0;
            if (searched == null && item.OriginalTitle != null)
            {
                searched = profile.SearchTerms.FirstOrDefault(t => Fits(t, item.OriginalTitle));
                searchWeight = searched != null ? 0.10 : 0;
            }

            var quality = Math.Clamp((item.VoteAverage - 5) / 4, 0, 1) * Math.Min(1, Math.Log10(item.VoteCount + 1) / 4);
            var popularity = Math.Min(1, Math.Log10(item.Popularity + 1) / 2.5);
            var year = item.Year ?? today.Year;
            var recency = Math.Clamp(1 - ((today.Year - year) / 40.0), 0, 1);
            var related = seenPart != null || searched != null;
            var upcoming = !item.Released && related ? 0.05 : 0;
            var score = (0.40 * genre) + (0.25 * series) + searchWeight + (0.15 * quality) + (0.10 * popularity) + (0.10 * recency) + upcoming;

            // Unveröffentlichte ohne Reihen- oder Suchbezug nicht vor Erschienenes mit Bezug; Termine über ein Jahr hinaus deutlich abwerten
            if (!item.Released)
            {
                score *= related ? 1 : 0.8;
                if (DateOnly.TryParse(item.ReleaseDate, CultureInfo.InvariantCulture, out var date) ? date > today.AddDays(365) : (item.Year ?? 0) > today.Year + 1)
                {
                    score *= 0.5;
                }
            }

            string? reason = null;
            if (seenPart != null)
            {
                reason = "Weil du „" + seenPart + "“ gesehen hast";
            }
            else if (searched != null)
            {
                reason = "Du hast nach „" + searched + "“ gesucht";
            }
            else if (genre >= 0.5)
            {
                // Nur die ersten beiden (Haupt-)Genres und nur, wenn sie dem Benutzer wirklich liegen
                var top = item.Genres.Take(2).Where(g => profile.Genres.TryGetValue(g, out var w) && w >= 0.6).ToList();
                reason = top.Count > 0 ? "Passt zu deinen Genres: " + string.Join(", ", top) : null;
            }

            return (Math.Min(1, score), reason);
        }

        /// <summary>
        /// Sets <see cref="DiscoverItem.MatchScore"/> and <see cref="DiscoverItem.Reason"/> and orders the titles: best fit first,
        /// then by popularity.
        /// </summary>
        /// <param name="items">The missing titles.</param>
        /// <param name="profile">The user's taste.</param>
        /// <param name="today">Today.</param>
        /// <returns>The ordered titles.</returns>
        public static List<DiscoverItem> Rank(IEnumerable<DiscoverItem> items, TasteProfile profile, DateOnly today)
        {
            ArgumentNullException.ThrowIfNull(items);
            var scored = new List<(DiscoverItem Item, double Score)>();
            foreach (var item in items)
            {
                var (score, reason) = Score(item, profile, today);
                item.MatchScore = (int)Math.Round(score * 100);
                item.Reason = reason;
                scored.Add((item, score));
            }

            return scored.OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.Popularity).Select(x => x.Item).ToList();
        }

        /// <summary>
        /// Gets whether a search term really names a title: all meaningful words of the term are whole words of the title,
        /// or the title starts with the term; at least 4 letters („clev“ fits no „clever“, „sword“ no „Merlin und Mim“).
        /// </summary>
        /// <param name="term">The search term.</param>
        /// <param name="title">The title.</param>
        /// <returns>True if it fits.</returns>
        public static bool Fits(string term, string title)
        {
            var wanted = SearchTerms.MeaningfulWords(term);
            if (wanted.Sum(w => w.Length) < 4)
            {
                return false;
            }

            var words = SearchTerms.Words(title);
            var all = SearchTerms.Words(term);
            var startsWith = all.Count > 0 && all.Count <= words.Count && all.Select((w, i) => w == words[i]).All(x => x);
            return startsWith || wanted.All(w => words.Contains(w));
        }

        /// <summary>
        /// Keeps the search terms that count: meaningful ones, and of typing states („clev“, „cleva“, „clevatess“) only the longest.
        /// </summary>
        /// <param name="terms">Search terms, newest first.</param>
        /// <returns>The terms.</returns>
        public static List<string> FinalTerms(IEnumerable<string> terms)
        {
            ArgumentNullException.ThrowIfNull(terms);
            var list = terms.Where(SearchTerms.IsMeaningful).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return list.Where(t => !list.Any(o => o.Length > t.Length && SearchTerms.IsTypingStep(o, t))).ToList();
        }

        /// <summary>
        /// Gets the taste of a user (cached for 10 minutes).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>The profile.</returns>
        public TasteProfile GetProfile(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            var now = DateTime.UtcNow;
            if (_profiles.TryGetValue(user.Id, out var cached) && now - cached.Created < ProfileLifetime)
            {
                return cached.Profile;
            }

            var profile = BuildProfile(user, now);
            _profiles[user.Id] = (now, profile);
            return profile;
        }

        private static double GenrePosition(int index) => index switch
        {
            0 => 1.0,
            1 => 0.8,
            _ => 0.4
        };

        private TasteProfile BuildProfile(User user, DateTime now)
        {
            var counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var collections = new Dictionary<int, string>();
            void Count(BaseItem item, double weight)
            {
                foreach (var genre in item.Genres ?? Array.Empty<string>())
                {
                    counts[genre] = counts.GetValueOrDefault(genre) + weight;
                }
            }

            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxPlayed
            });
            foreach (var movie in movies)
            {
                var data = _userDataManager.GetUserData(user, movie);
                Count(movie, 1 + (data?.IsFavorite == true ? 1 : 0) + (data?.Likes == true ? 0.5 : 0) - (data?.Likes == false ? 1 : 0));
                if (movie.ProviderIds.TryGetValue("TmdbCollection", out var raw) && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var collection))
                {
                    collections.TryAdd(collection, movie.Name);
                }
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxPlayed
            });
            foreach (var group in episodes.OfType<Episode>().Where(e => e.SeriesId != Guid.Empty).GroupBy(e => e.SeriesId))
            {
                if (_libraryManager.GetItemById(group.Key) is Series show)
                {
                    var data = _userDataManager.GetUserData(user, show);
                    Count(show, Math.Min(3, group.Count() / 3.0) + (data?.IsFavorite == true ? 1 : 0));
                }
            }

            var max = counts.Count == 0 ? 1 : Math.Max(1, counts.Values.Max());
            var genres = counts.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value / max, StringComparer.OrdinalIgnoreCase);
            var terms = _behavior == null || !_behavior.IsEnabled(user.Id)
                ? new List<string>()
                : FinalTerms(_behavior.Get(user.Id).Searches.Where(s => now - s.At <= SearchWindow).OrderByDescending(s => s.At).Select(s => s.Term));
            return new TasteProfile(genres, collections, terms);
        }

        /// <summary>
        /// Der Geschmack eines Benutzers für fehlende Titel.
        /// </summary>
        /// <param name="Genres">Genre → weight (0–1, most watched = 1).</param>
        /// <param name="Collections">TMDB collection → title of a watched part.</param>
        /// <param name="SearchTerms">Search terms of the last 60 days.</param>
        public sealed record TasteProfile(IReadOnlyDictionary<string, double> Genres, IReadOnlyDictionary<int, string> Collections, IReadOnlyList<string> SearchTerms);
    }
}
