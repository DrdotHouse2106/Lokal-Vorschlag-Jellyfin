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
        /// Gets the titles of a season for a user: unwatched first, then by taste and rating; at most <see cref="MaxItems"/>.
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
                var perSeries = new Dictionary<Guid, int>();
                var result = new List<BaseItem>();
                foreach (var item in items
                    .OrderBy(i => lookup.Get(i)?.Played == true ? 1 : 0)
                    .ThenBy(i => taste.TryGetValue(i is Episode e ? e.SeriesId : i.Id, out var position) ? position : int.MaxValue)
                    .ThenByDescending(i => i.CommunityRating ?? 0)
                    .ThenBy(i => i.SortName ?? i.Name, StringComparer.Ordinal))
                {
                    if (item is Episode episode)
                    {
                        var count = perSeries.GetValueOrDefault(episode.SeriesId);
                        if (count >= MaxEpisodesPerSeries)
                        {
                            continue;
                        }

                        perSeries[episode.SeriesId] = count + 1;
                    }

                    result.Add(item);
                    if (result.Count >= MaxItems)
                    {
                        break;
                    }
                }

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
                    if (season.MatchesTitle(episode.Name) && !ids.Contains(episode.Id))
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
