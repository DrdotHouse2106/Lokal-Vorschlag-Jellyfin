using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Collections;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Moments
{
    /// <summary>
    /// Zeitabhängige Zeilen: „Neu diese Woche“, „Vor dem Kinostart“, „Heute vor X Jahren“, „Vor einem Jahr hast du …“ und
    /// „Läuft heute im TV – hier ohne Werbung“. Alle Abfragen laufen im Namen des Benutzers, damit nur Titel aus seinen
    /// Bibliotheken und innerhalb seiner Altersfreigabe erscheinen.
    /// </summary>
    public class MomentsService
    {
        /// <summary>Key of „Neu diese Woche“.</summary>
        public const string NewThisWeek = "newthisweek";

        /// <summary>Key of „Vor dem Kinostart“.</summary>
        public const string Cinema = "cinema";

        /// <summary>Key of „Heute vor X Jahren“.</summary>
        public const string Anniversary = "anniversary";

        /// <summary>Key of „Vor einem Jahr hast du …“.</summary>
        public const string YearAgo = "yearago";

        /// <summary>Key of „Läuft heute im TV“.</summary>
        public const string OnTvToday = "ontv";

        /// <summary>Most titles in a row.</summary>
        public const int MaxItems = 20;

        /// <summary>Most titles in „Heute vor X Jahren“ (kept small on purpose).</summary>
        public const int MaxAnniversaries = 6;

        /// <summary>Days ahead for „Vor dem Kinostart“.</summary>
        public const int CinemaDays = 28;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        private readonly ILogger<MomentsService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly RecommendationStore _store;
        private readonly MovieSeriesService _movieSeries;
        private readonly ILiveTvManager _liveTvManager;
        private readonly ConcurrentDictionary<(Guid User, string Key, int Offset), (DateTime Created, MomentRow Row)> _cache = new ConcurrentDictionary<(Guid User, string Key, int Offset), (DateTime Created, MomentRow Row)>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MomentsService"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="store">Stored recommendations (taste order).</param>
        /// <param name="movieSeries">Movie series (upcoming parts).</param>
        /// <param name="liveTvManager">Live TV manager.</param>
        public MomentsService(ILogger<MomentsService> logger, ILibraryManager libraryManager, IUserDataManager userDataManager, RecommendationStore store, MovieSeriesService movieSeries, ILiveTvManager liveTvManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _movieSeries = movieSeries ?? throw new ArgumentNullException(nameof(movieSeries));
            _liveTvManager = liveTvManager ?? throw new ArgumentNullException(nameof(liveTvManager));
        }

        /// <summary>Gets all row keys in display order.</summary>
        public static IReadOnlyList<string> Keys { get; } = new[] { OnTvToday, NewThisWeek, Cinema, Anniversary, YearAgo };

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets whether a row is switched on.
        /// </summary>
        /// <param name="key">Row key.</param>
        /// <param name="config">Configuration.</param>
        /// <returns>True if on.</returns>
        public static bool IsEnabled(string key, PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return key switch
            {
                NewThisWeek => config.MomentsNewThisWeek,
                Cinema => config.MomentsCinema,
                Anniversary => config.MomentsAnniversaries,
                YearAgo => config.MomentsYearAgo,
                OnTvToday => config.MomentsOnTvToday,
                _ => false
            };
        }

        /// <summary>
        /// Gets the round anniversary of a release on a day: 10, 20, 25, 30 … years (multiples of ten plus 25 and 75), or null.
        /// A release on 29 February counts on 28 February in other years.
        /// </summary>
        /// <param name="premiere">Release date.</param>
        /// <param name="today">The local day.</param>
        /// <returns>Years, or null.</returns>
        public static int? RoundAnniversary(DateTime premiere, DateOnly today)
        {
            var years = today.Year - premiere.Year;
            if (years < 10 || (years % 10 != 0 && years % 25 != 0))
            {
                return null;
            }

            var day = premiere.Month == 2 && premiere.Day == 29 && !DateTime.IsLeapYear(today.Year) ? 28 : premiere.Day;
            return premiere.Month == today.Month && day == today.Day ? years : null;
        }

        /// <summary>
        /// Normalizes a title for comparing TV listings with the library: lower case, letters and digits only, accents removed.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <returns>The normalized title.</returns>
        public static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(title.Length);
            foreach (var c in title.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString().Replace("ß", "ss", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the time text for a broadcast: „Heute 20:15“ or „Heute Nacht 00:30“.
        /// </summary>
        /// <param name="startLocal">Local start.</param>
        /// <param name="today">The local day.</param>
        /// <returns>The text.</returns>
        public static string BroadcastTime(DateTime startLocal, DateOnly today)
        {
            var prefix = DateOnly.FromDateTime(startLocal) > today ? "Heute Nacht " : "Heute ";
            return prefix + startLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the rows that are switched on and not empty, for the app's time zone.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone, optional.</param>
        /// <param name="only">Only this row, optional.</param>
        /// <returns>The rows.</returns>
        public IReadOnlyList<MomentRow> GetRows(User user, int? utcOffsetMinutes, string? only = null)
        {
            ArgumentNullException.ThrowIfNull(user);
            var config = Config;
            return Keys
                .Where(k => (only == null || string.Equals(k, only, StringComparison.OrdinalIgnoreCase)) && IsEnabled(k, config))
                .Select(k => GetRow(user, k, utcOffsetMinutes))
                .Where(r => r.Items.Count > 0)
                .ToList();
        }

        /// <summary>
        /// Gets one row (empty if switched off, nothing fits or an error occurs); cached for ten minutes.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="key">Row key.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone, optional.</param>
        /// <returns>The row.</returns>
        public MomentRow GetRow(User user, string key, int? utcOffsetMinutes)
        {
            ArgumentNullException.ThrowIfNull(user);
            var config = Config;
            if (!IsEnabled(key, config))
            {
                return new MomentRow { Key = key };
            }

            var offset = BehaviorService.ResolveOffset(utcOffsetMinutes);
            var cacheKey = (user.Id, key, offset);
            if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.Created < CacheDuration)
            {
                return cached.Row;
            }

            var nowLocal = DateTime.UtcNow.AddMinutes(offset);
            var today = DateOnly.FromDateTime(nowLocal);
            MomentRow row;
            try
            {
                row = key switch
                {
                    NewThisWeek => BuildNewThisWeek(user, config, offset),
                    Cinema => BuildCinema(user, today),
                    Anniversary => BuildAnniversary(user, today),
                    YearAgo => BuildYearAgo(user, offset),
                    OnTvToday => BuildOnTv(user, offset, today),
                    _ => new MomentRow { Key = key }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zeitabhängige Zeile {Key} konnte nicht erstellt werden", key);
                row = new MomentRow { Key = key };
            }

            _cache[cacheKey] = (DateTime.UtcNow, row);
            return row;
        }

        /// <summary>
        /// Forgets all cached rows.
        /// </summary>
        public void Invalidate() => _cache.Clear();

        // Sendung → Titel der Bibliothek: erst über IMDb/TMDB/TVDB, sonst über den Titel (Filme zusätzlich über das Jahr ±1)
        private static BaseItem? Match(LiveTvProgram program, Dictionary<string, BaseItem> byProvider, Dictionary<string, List<BaseItem>> byTitle)
        {
            var series = program.IsSeries && !program.IsMovie;
            var prefix = series ? "s:" : "m:";
            foreach (var (provider, id) in program.ProviderIds)
            {
                if (!string.IsNullOrEmpty(id) && byProvider.TryGetValue(prefix + provider + ":" + id, out var byId))
                {
                    return byId;
                }
            }

            if (!byTitle.TryGetValue(NormalizeTitle(program.Name), out var candidates))
            {
                return null;
            }

            if (series)
            {
                return candidates.FirstOrDefault(c => c is Series);
            }

            if (!program.IsMovie)
            {
                return null;
            }

            var movies = candidates.Where(c => c is not Series).ToList();
            if (program.ProductionYear is int year)
            {
                return movies.FirstOrDefault(m => m.ProductionYear is int y && Math.Abs(y - year) <= 1);
            }

            return movies.Count == 1 ? movies[0] : null;
        }

        // Position in den gespeicherten Empfehlungen (kleiner = passt besser), Serien und Filme getrennt
        private Dictionary<Guid, int> TastePositions(User user)
        {
            var recs = _store.Get(user.Id);
            if (recs == null)
            {
                return new Dictionary<Guid, int>();
            }

            return recs.Movies.Select((id, index) => (id, index))
                .Concat(recs.Series.Select((id, index) => (id, index)))
                .GroupBy(x => x.id)
                .ToDictionary(g => g.Key, g => g.Min(x => x.index));
        }

        private MomentRow BuildNewThisWeek(User user, PluginConfiguration config, int offset)
        {
            var days = Math.Clamp(config.NewThisWeekDays, 7, 30);
            var since = DateTime.UtcNow.AddDays(-days);
            var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                MinDateCreated = since,
                IsPlayed = false,
                Recursive = true,
                IsVirtualItem = false
            });

            var row = new MomentRow { Key = NewThisWeek, Title = days <= 7 ? "Neu diese Woche" : "Neu in den letzten " + days + " Tagen" };
            var candidates = new List<(BaseItem Item, DateTime Added, string Reason)>();
            foreach (var movie in items.Where(i => i is not Episode))
            {
                candidates.Add((movie, movie.DateCreated, "Neu seit " + movie.DateCreated.AddMinutes(offset).ToString("dd.MM.", CultureInfo.InvariantCulture)));
            }

            foreach (var group in items.OfType<Episode>().Where(e => e.SeriesId != Guid.Empty).GroupBy(e => e.SeriesId))
            {
                if (_libraryManager.GetItemById(group.Key) is not Series series || !series.IsVisibleStandalone(user))
                {
                    continue;
                }

                var count = group.Count();
                candidates.Add((series, group.Max(e => e.DateCreated), count == 1 ? "1 neue Folge" : count + " neue Folgen"));
            }

            var taste = TastePositions(user);
            foreach (var c in candidates
                .OrderBy(c => taste.TryGetValue(c.Item.Id, out var position) ? position : int.MaxValue)
                .ThenByDescending(c => c.Added)
                .Take(MaxItems))
            {
                row.Add(c.Item, c.Reason);
            }

            return row;
        }

        private MomentRow BuildCinema(User user, DateOnly today)
        {
            var row = new MomentRow { Key = Cinema, Title = "Vor dem Kinostart: Hol die Reihe nach" };
            foreach (var (series, title, date) in _movieSeries.GetUpcoming(user, today, CinemaDays))
            {
                if (series.Parts.Count == 0)
                {
                    continue;
                }

                // Ganz gesehen: der letzte Teil zum Auffrischen
                var refresh = series.NextIndex < 0 || series.NextIndex >= series.Parts.Count;
                var part = refresh ? series.Parts[series.Parts.Count - 1] : series.Parts[series.NextIndex];
                var days = date.DayNumber - today.DayNumber;
                var when = days == 1 ? "morgen" : days <= 7 ? "in " + days + " Tagen" : "ab " + date.ToString("dd.MM.", CultureInfo.InvariantCulture);
                row.Add(part, "„" + title + "“ " + when + " im Kino" + (refresh ? " – zum Auffrischen" : string.Empty));
                if (row.Items.Count >= MaxItems)
                {
                    break;
                }
            }

            return row;
        }

        private MomentRow BuildAnniversary(User user, DateOnly today)
        {
            var row = new MomentRow { Key = Anniversary, Title = "Heute vor … Jahren" };
            var found = new List<(BaseItem Item, int Years)>();
            foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false,
                MaxPremiereDate = DateTime.UtcNow.AddYears(-9)
            }))
            {
                if (item.PremiereDate is DateTime premiere && RoundAnniversary(premiere, today) is int years)
                {
                    found.Add((item, years));
                }
            }

            var taste = TastePositions(user);
            foreach (var (item, years) in found
                .OrderBy(f => taste.TryGetValue(f.Item.Id, out var position) ? position : int.MaxValue)
                .ThenByDescending(f => f.Item.CommunityRating ?? 0)
                .Take(MaxAnniversaries))
            {
                row.Add(item, "Heute vor " + years + " Jahren " + (item is Series ? "gestartet" : "erschienen"));
            }

            if (row.Items.Count > 0)
            {
                var distinct = found.Select(f => f.Years).Distinct().ToList();
                row.Title = distinct.Count == 1 ? "Heute vor " + distinct[0] + " Jahren" : "Heute vor X Jahren";
            }

            return row;
        }

        private MomentRow BuildYearAgo(User user, int offset)
        {
            var row = new MomentRow { Key = YearAgo, Title = "Vor einem Jahr hast du …" };
            var from = DateTime.UtcNow.AddDays(-400);
            var to = DateTime.UtcNow.AddDays(-330);
            var played = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                IsVirtualItem = false
            });
            var lookup = UserDataLookup.Load(_userDataManager, user, played);
            var seen = new HashSet<Guid>();
            var found = new List<(BaseItem Item, DateTime When)>();
            foreach (var item in played)
            {
                var data = lookup.Get(item);
                if (data?.LastPlayedDate is not DateTime last || last < from || last > to || data.Likes == false)
                {
                    continue;
                }

                BaseItem? target = item is Episode episode ? _libraryManager.GetItemById(episode.SeriesId) as Series : item;
                if (target == null || !seen.Add(target.Id) || !target.IsVisibleStandalone(user))
                {
                    continue;
                }

                found.Add((target, last));
            }

            // Nichts, was seitdem wieder gelaufen ist (dann ist es keine Erinnerung mehr)
            var recent = UserDataLookup.Load(_userDataManager, user, found.Select(f => f.Item));
            foreach (var (item, when) in found
                .Where(f => NotRecent(f.Item))
                .OrderBy(f => Math.Abs((f.When - DateTime.UtcNow.AddYears(-1)).TotalDays))
                .Take(MaxItems))
            {
                row.Add(item, item is Series ? "Vor einem Jahr geschaut" : "Vor einem Jahr gesehen – am " + when.AddMinutes(offset).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            }

            return row;

            bool NotRecent(BaseItem item) => item is Series || recent.Get(item)?.LastPlayedDate is not DateTime d || d <= to;
        }

        private MomentRow BuildOnTv(User user, int offset, DateOnly today)
        {
            var row = new MomentRow { Key = OnTvToday, Title = "Läuft heute im TV – hier ohne Werbung" };
            if (!user.HasPermission(PermissionKind.EnableLiveTvAccess))
            {
                return row;
            }

            var now = DateTime.UtcNow;
            var end = today.AddDays(1).ToDateTime(new TimeOnly(2, 0)).AddMinutes(-offset);
            var liveTvFolder = _liveTvManager.GetInternalLiveTvFolder(CancellationToken.None);
            var programs = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.LiveTvProgram },
                MinStartDate = now,
                MaxStartDate = DateTime.SpecifyKind(end, DateTimeKind.Utc),
                TopParentIds = new[] { liveTvFolder.Id },
                OrderBy = new[] { (ItemSortBy.StartDate, SortOrder.Ascending) }
            }).OfType<LiveTvProgram>().ToList();
            if (programs.Count == 0)
            {
                return row;
            }

            var library = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            });
            var byProvider = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
            var byTitle = new Dictionary<string, List<BaseItem>>(StringComparer.Ordinal);
            foreach (var item in library)
            {
                foreach (var (provider, id) in item.ProviderIds)
                {
                    if (!string.IsNullOrEmpty(id) && (provider == "Imdb" || provider == "Tmdb" || provider == "Tvdb"))
                    {
                        byProvider.TryAdd((item is Series ? "s:" : "m:") + provider + ":" + id, item);
                    }
                }

                var normalized = NormalizeTitle(item.Name);
                if (normalized.Length >= 2)
                {
                    if (!byTitle.TryGetValue(normalized, out var list))
                    {
                        byTitle[normalized] = list = new List<BaseItem>();
                    }

                    list.Add(item);
                }
            }

            var channels = new Dictionary<Guid, string?>();
            foreach (var program in programs)
            {
                var match = Match(program, byProvider, byTitle);
                if (match == null || row.Items.Any(i => i.Id == match.Id))
                {
                    continue;
                }

                if (!channels.TryGetValue(program.ChannelId, out var channel))
                {
                    channels[program.ChannelId] = channel = _libraryManager.GetItemById(program.ChannelId)?.Name;
                }

                var time = BroadcastTime(program.StartDate.AddMinutes(offset), today);
                row.Add(match, time + (string.IsNullOrEmpty(channel) ? string.Empty : " auf " + channel) + " – hier ohne Werbung");
                if (row.Items.Count >= MaxItems)
                {
                    break;
                }
            }

            return row;
        }

        /// <summary>A row with titles and their reason lines.</summary>
        public class MomentRow
        {
            private readonly List<BaseItem> _items = new List<BaseItem>();

            /// <summary>Gets or sets the row key.</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets the titles.</summary>
            public IReadOnlyList<BaseItem> Items => _items;

            /// <summary>Gets the reason line per title (ID without dashes).</summary>
            public Dictionary<string, string> Reasons { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>
            /// Adds a title.
            /// </summary>
            /// <param name="item">The title.</param>
            /// <param name="reason">The reason line.</param>
            public void Add(BaseItem item, string reason)
            {
                ArgumentNullException.ThrowIfNull(item);
                _items.Add(item);
                Reasons[item.Id.ToString("N")] = reason;
            }
        }
    }
}
