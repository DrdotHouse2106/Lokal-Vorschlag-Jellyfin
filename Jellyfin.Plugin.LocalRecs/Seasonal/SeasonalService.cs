using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Seasonal
{
    /// <summary>
    /// „Passend zur Jahreszeit“: in der Weihnachtszeit Weihnachtsfilme, an Halloween Halloween-Titel usw.
    /// Erkannt über TMDB-Stichwörter bzw. gleichlautende Tags, ganze Wörter im Titel und Folgen bzw. Specials mit solchen
    /// Titeln. Jeder Benutzer sieht nur Titel aus seinen Bibliotheken und innerhalb seiner Altersfreigabe.
    /// </summary>
    public class SeasonalService
    {
        /// <summary>Most titles in the row.</summary>
        public const int MaxItems = 20;

        /// <summary>Most episodes of the same series.</summary>
        public const int MaxEpisodesPerSeries = 3;

        private static readonly TimeSpan CandidateCacheDuration = TimeSpan.FromHours(1);

        private readonly ILogger<SeasonalService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly SeasonalKeywordLookup _keywords;
        private readonly RecommendationStore _store;
        private readonly object _lock = new object();
        private (string Season, long Version, DateTime Created, List<Guid> Ids)? _candidates;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeasonalService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="keywords">TMDB keywords.</param>
        /// <param name="store">Stored recommendations (taste order).</param>
        public SeasonalService(ILogger<SeasonalService> logger, ILibraryManager libraryManager, IUserDataManager userDataManager, SeasonalKeywordLookup keywords, RecommendationStore store)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the current season for the app's time zone, or null outside all windows (or when switched off).
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone, optional.</param>
        /// <returns>The season, or null.</returns>
        public static SeasonCalendar.Season? CurrentSeason(int? utcOffsetMinutes)
        {
            var config = Config;
            if (!config.EnableSeasonal)
            {
                return null;
            }

            var day = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(BehaviorService.ResolveOffset(utcOffsetMinutes)));
            return SeasonCalendar.SeasonOf(day, config.SeasonalValentine);
        }

        /// <summary>
        /// Gets whether a movie or series belongs to the season (TMDB keywords, tags or a whole word in the title).
        /// </summary>
        /// <param name="season">The season.</param>
        /// <param name="title">The title.</param>
        /// <param name="tags">Jellyfin tags.</param>
        /// <param name="keywords">TMDB keywords (or null if not looked up).</param>
        /// <returns>True if it belongs to the season.</returns>
        public static bool Matches(SeasonCalendar.Season season, string? title, IEnumerable<string>? tags, IEnumerable<string>? keywords)
        {
            ArgumentNullException.ThrowIfNull(season);
            return season.MatchesKeywords(keywords) || season.MatchesKeywords(tags) || season.MatchesTitle(title);
        }

        /// <summary>
        /// Gets whether the file name of a special (season 0) agrees with the season. Specials are often numbered differently
        /// at TVDB and TMDB, so the title in Jellyfin may belong to another file. A file name without its own title counts as agreeing.
        /// </summary>
        /// <param name="season">The season.</param>
        /// <param name="path">Path of the file.</param>
        /// <param name="seriesName">Name of the series (removed from the file name).</param>
        /// <returns>True if the file name has no title of its own or contains a word of the season.</returns>
        public static bool FileNameAgrees(SeasonCalendar.Season season, string? path, string? seriesName)
        {
            ArgumentNullException.ThrowIfNull(season);
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(path).Replace('.', ' ').Replace('_', ' ');
            if (!string.IsNullOrWhiteSpace(seriesName))
            {
                name = name.Replace(seriesName, " ", StringComparison.OrdinalIgnoreCase);
            }

            name = System.Text.RegularExpressions.Regex.Replace(name, @"\b[sS]\d{1,2}\s*[eE]\d{1,3}\b|\b\d{1,2}x\d{1,3}\b|\b(19|20)\d{2}\b|\b\d{3,4}p\b", " ");
            return name.Count(char.IsLetter) < 4 || season.MatchesTitle(name);
        }

        /// <summary>
        /// Orders the titles of a season: unwatched first, movies and series before episodes, then by taste and rating. First one episode
        /// per series, further episodes (up to <see cref="MaxEpisodesPerSeries"/>) behind all of them.
        /// </summary>
        /// <param name="items">The titles.</param>
        /// <param name="played">Whether a title was watched.</param>
        /// <param name="taste">Position in the recommendations (smaller fits better).</param>
        /// <returns>The ordered titles, at most <see cref="MaxItems"/>.</returns>
        public static List<BaseItem> Order(IEnumerable<BaseItem> items, Func<BaseItem, bool> played, IReadOnlyDictionary<Guid, int> taste)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(played);
            ArgumentNullException.ThrowIfNull(taste);
            var front = new List<BaseItem>();
            var later = new List<BaseItem>();
            var perSeries = new Dictionary<Guid, int>();
            foreach (var item in items
                .OrderBy(i => played(i) ? 1 : 0)
                .ThenBy(i => i is Episode ? 1 : 0)
                .ThenBy(i => taste.TryGetValue(i is Episode e ? e.SeriesId : i.Id, out var position) ? position : int.MaxValue)
                .ThenByDescending(i => i.CommunityRating ?? 0)
                .ThenBy(i => i.SortName ?? i.Name, StringComparer.Ordinal))
            {
                if (item is not Episode episode)
                {
                    front.Add(item);
                    continue;
                }

                var count = perSeries.GetValueOrDefault(episode.SeriesId);
                if (count >= MaxEpisodesPerSeries)
                {
                    continue;
                }

                perSeries[episode.SeriesId] = count + 1;
                (count == 0 ? front : later).Add(item);
            }

            // Weitere Folgen derselben Serie erst hinter allen ersten Folgen der anderen Serien
            front.AddRange(later);
            return front.Take(MaxItems).ToList();
        }

        /// <summary>
        /// Gets the titles of a season for a user (see <see cref="Order"/>), at most <see cref="MaxItems"/>.
        /// Empty when fewer than the configured minimum are found.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="season">The season.</param>
        /// <returns>The titles.</returns>
        public IReadOnlyList<BaseItem> GetItems(User user, SeasonCalendar.Season season)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(season);
            try
            {
                var items = Candidates(season)
                    .Select(id => _libraryManager.GetItemById(id))
                    .OfType<BaseItem>()
                    .Where(i => i.IsVisibleStandalone(user))
                    .ToList();
                var lookup = UserDataLookup.Load(_userDataManager, user, items);
                var taste = _store.Get(user.Id) is { } recs
                    ? recs.Movies.Concat(recs.Series).Select((id, index) => (id, index)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.Min(x => x.index))
                    : new Dictionary<Guid, int>();
                var result = Order(items, i => lookup.Get(i)?.Played == true, taste);
                return result.Count >= Math.Max(1, Config.SeasonalMinItems) ? result : Array.Empty<BaseItem>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Titel zur Jahreszeit konnten nicht ausgewählt werden");
                return Array.Empty<BaseItem>();
            }
        }

        // Alle Titel der Jahreszeit im Bestand (ohne Benutzer, Sichtbarkeit wird je Benutzer geprüft), eine Stunde zwischengespeichert
        private List<Guid> Candidates(SeasonCalendar.Season season)
        {
            var version = _keywords.Version;
            lock (_lock)
            {
                if (_candidates is { } cached && cached.Season == season.Id && cached.Version == version && DateTime.UtcNow - cached.Created < CandidateCacheDuration)
                {
                    return cached.Ids;
                }
            }

            var ids = new List<Guid>();
            foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            }))
            {
                var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
                var keywords = string.IsNullOrEmpty(tmdb) ? null : _keywords.Get(SeasonalKeywordLookup.Key(item is Series, tmdb));
                if (Matches(season, item.Name, item.Tags, keywords))
                {
                    ids.Add(item.Id);
                }
            }

            // Folgen und Specials mit einem Wort der Jahreszeit im Titel (Vorauswahl über Teilwörter, dann ganze Wörter)
            foreach (var stem in season.Stems)
            {
                foreach (var episode in _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    Recursive = true,
                    IsVirtualItem = false,
                    NameContains = stem
                }))
                {
                    // Specials (Staffel 0) nur, wenn auch der Dateiname passt – die Nummerierung weicht zwischen TVDB und TMDB oft ab
                    var special = episode is Episode e && e.ParentIndexNumber == 0;
                    if (season.MatchesTitle(episode.Name) && !ids.Contains(episode.Id)
                        && (!special || FileNameAgrees(season, episode.Path, ((Episode)episode).SeriesName)))
                    {
                        ids.Add(episode.Id);
                    }
                }
            }

            lock (_lock)
            {
                _candidates = (season.Id, version, DateTime.UtcNow, ids);
            }

            return ids;
        }
    }
}
