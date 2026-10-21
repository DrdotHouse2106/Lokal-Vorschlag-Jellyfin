using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LocalRecs.Banner
{
    /// <summary>
    /// Der Banner des Tages je Benutzer, für alle Geräte gleich. Gemischt aus zwei Töpfen:
    /// <list type="bullet">
    /// <item>Entdecken: die besten Film- und Serien-Empfehlungen.</item>
    /// <item>Gerade passend: angefangene Serien mit ungesehenen (auch neuen) Folgen und angefangene Filme.</item>
    /// </list>
    /// Am Tag fest, am nächsten Tag neu. Abends unter der Woche zählen kürzere Filme mehr. Titel aus dem Banner der
    /// letzten Tage, mehrfach gezeigte aber nie geöffnete und lange unbeachtete Empfehlungen kommen seltener dran;
    /// mit „Später“ weggelegte Titel gar nicht.
    /// </summary>
    public class BannerService
    {
        /// <summary>Share of the banner for titles that fit right now (started series and movies).</summary>
        public const double ContinueShare = 0.4;

        /// <summary>Default days a title stays out of the banner after „Später“.</summary>
        public const int DefaultLaterDays = 21;

        /// <summary>Movies up to this runtime count as short (minutes).</summary>
        public const int ShortMovieMinutes = 100;

        /// <summary>Reason of a started series.</summary>
        public const string ReasonContinueSeries = "Weiterschauen";

        /// <summary>Reason of a series with new episodes.</summary>
        public const string ReasonNewEpisodes = "Neue Folgen";

        /// <summary>Reason of a started movie.</summary>
        public const string ReasonContinueMovie = "Fortsetzen";

        private static readonly TimeSpan ContinueWindow = TimeSpan.FromDays(60);

        private readonly RecommendationSectionHandler _recommendations;
        private readonly BannerStore _store;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly FeedbackStore? _feedback;
        private readonly BehaviorStore? _behavior;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerService"/> class.
        /// </summary>
        /// <param name="recommendations">Movie and series recommendations.</param>
        /// <param name="store">Banner of the last days and „Später“.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="feedback">What was recommended since when, optional.</param>
        /// <param name="behavior">Opened titles, optional.</param>
        public BannerService(
            RecommendationSectionHandler recommendations,
            BannerStore store,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            FeedbackStore? feedback = null,
            BehaviorStore? behavior = null)
        {
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _feedback = feedback;
            _behavior = behavior;
        }

        /// <summary>
        /// Gets the factor for a title that was in the banner some days ago: yesterday 0.15, two days ago 0.3, within a week 0.6.
        /// </summary>
        /// <param name="daysAgo">Days since it was last in the banner, or null.</param>
        /// <returns>The factor.</returns>
        public static double RecentFactor(int? daysAgo) => daysAgo switch
        {
            null => 1,
            <= 1 => 0.15,
            2 => 0.3,
            <= 7 => 0.6,
            _ => 1
        };

        /// <summary>
        /// Gets the factor for a recommendation that has been in the rows unwatched for some days: from 14 days 0.5, from 30 days 0.3.
        /// </summary>
        /// <param name="days">Days in the rows without being watched, or null.</param>
        /// <returns>The factor.</returns>
        public static double IgnoredFactor(double? days) => days switch
        {
            null => 1,
            >= 30 => 0.3,
            >= 14 => 0.5,
            _ => 1
        };

        /// <summary>
        /// Gets the factor for a title that was in the banner on several of the last 7 days but never opened: from 2 days 0.5, from 4 days 0.25.
        /// </summary>
        /// <param name="shownDays">Days in the banner within the last week.</param>
        /// <param name="opened">True if the user opened or played it.</param>
        /// <returns>The factor.</returns>
        public static double NeverOpenedFactor(int shownDays, bool opened) => opened ? 1 : shownDays switch
        {
            >= 4 => 0.25,
            >= 2 => 0.5,
            _ => 1
        };

        /// <summary>
        /// Gets whether short movies are preferred: evenings (17–22 local) from Sunday to Thursday.
        /// </summary>
        /// <param name="localTime">Local time.</param>
        /// <returns>True for a weekday evening.</returns>
        public static bool PreferShort(DateTime localTime)
        {
            return DaypartProfile.DaypartOf(localTime.Hour) == 2 && localTime.DayOfWeek is not DayOfWeek.Friday and not DayOfWeek.Saturday;
        }

        /// <summary>
        /// Gets the factor of a movie by its runtime: on weekday evenings long movies (over <see cref="ShortMovieMinutes"/>) count half.
        /// </summary>
        /// <param name="runtimeMinutes">Runtime in minutes, or null.</param>
        /// <param name="preferShort">True on weekday evenings.</param>
        /// <returns>The factor.</returns>
        public static double RuntimeFactor(double? runtimeMinutes, bool preferShort) => preferShort && runtimeMinutes > ShortMovieMinutes ? 0.5 : 1;

        /// <summary>
        /// Gets how many banner places go to titles that fit right now.
        /// </summary>
        /// <param name="count">Size of the banner.</param>
        /// <param name="available">Available titles that fit right now.</param>
        /// <returns>Number of places.</returns>
        public static int ContinueSlots(int count, int available) => available <= 0 ? 0 : Math.Min(available, Math.Max(1, (int)Math.Round(count * ContinueShare)));

        /// <summary>
        /// Gets the banner of today for a user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone (for the day), optional.</param>
        /// <param name="count">Number of titles (1–12).</param>
        /// <returns>The titles with a short reason for titles that fit right now (empty otherwise).</returns>
        public IReadOnlyList<(BaseItem Item, string Reason)> GetBanner(User user, int? utcOffsetMinutes, int count)
        {
            ArgumentNullException.ThrowIfNull(user);
            count = Math.Clamp(count, 1, 12);
            var now = DateTime.UtcNow;
            var local = now.AddMinutes(BehaviorService.ResolveOffset(utcOffsetMinutes));
            var day = DateOnly.FromDateTime(local);
            var later = _store.GetLater(user.Id, now);

            var continueItems = ContinueCandidates(user, now);
            var reasons = continueItems.ToDictionary(c => c.Item.Id, c => c.Reason);

            // Schon für heute gewählt: dieselben Titel, solange sie sichtbar, ungesehen und nicht „Später“ sind;
            // weggefallene Plätze werden aufgefüllt
            var keep = (_store.Get(user.Id, day) ?? Array.Empty<Guid>())
                .Where(id => !later.ContainsKey(id))
                .Select(id => _libraryManager.GetItemById(id))
                .OfType<BaseItem>()
                .Where(i => Usable(user, i))
                .ToList();
            if (keep.Count >= count)
            {
                return keep.Take(count).Select(i => (i, reasons.GetValueOrDefault(i.Id) ?? string.Empty)).ToList();
            }

            var preferShort = PreferShort(local);
            var recent = _store.RecentlyShown(user.Id, day);
            var shownDays = _store.ShownDays(user.Id, day, 7);
            var opened = _behavior?.Get(user.Id).Views.Select(v => v.ItemId).ToHashSet() ?? new HashSet<Guid>();
            var shownSince = ShownSince(user.Id, now);
            double Factor(BaseItem item) =>
                RecentFactor(recent.TryGetValue(item.Id, out var ago) ? ago : null)
                * NeverOpenedFactor(shownDays.GetValueOrDefault(item.Id), opened.Contains(item.Id))
                * IgnoredFactor(shownSince.TryGetValue(item.Id.ToString("N"), out var since) ? (now - since).TotalDays : null)
                * (item is Movie ? RuntimeFactor(item.RunTimeTicks / (double)TimeSpan.TicksPerMinute, preferShort) : 1);

            var taken = keep.Select(i => i.Id).ToHashSet();
            bool Free(BaseItem item) => !taken.Contains(item.Id) && !later.ContainsKey(item.Id) && HasBackdrop(item);

            var movies = _recommendations.GetItems(user.Id, RecommendationSectionHandler.MoviesSection, utcOffsetMinutes);
            var series = _recommendations.GetItems(user.Id, RecommendationSectionHandler.SeriesSection, utcOffsetMinutes);
            var discover = BannerPicker.Interleave(movies.Take(BannerPicker.MaxCandidates / 2).ToList(), series.Take(BannerPicker.MaxCandidates / 2).ToList())
                .Where(Free)
                .ToList();
            var fitting = continueItems.Select(c => c.Item).Where(Free).ToList();

            var seed = BannerPicker.Seed(user.Id, day);
            var missing = count - keep.Count;
            var keptContinue = keep.Count(i => reasons.ContainsKey(i.Id));
            var continueCount = Math.Max(0, Math.Min(ContinueSlots(count, keptContinue + fitting.Count) - keptContinue, missing));
            var pickedContinue = BannerPicker.Pick(fitting, Factor, continueCount, seed);
            var discoverPool = discover.Where(i => !pickedContinue.Contains(i)).ToList();
            var pickedDiscover = BannerPicker.Pick(discoverPool, Factor, missing - pickedContinue.Count, seed ^ 0x5bd1e995);

            // Zu wenige Empfehlungen: mit weiteren passenden Titeln auffüllen
            if (pickedContinue.Count + pickedDiscover.Count < missing)
            {
                pickedContinue.AddRange(BannerPicker.Pick(fitting.Where(i => !pickedContinue.Contains(i)).ToList(), Factor, missing - pickedContinue.Count - pickedDiscover.Count, seed + 1));
            }

            // Reihenfolge: Entdecken und Gerade passend abwechselnd, Entdecken zuerst
            var result = new List<BaseItem>(keep);
            result.AddRange(BannerPicker.Interleave(pickedDiscover, pickedContinue));
            if (result.Count > 0)
            {
                _store.Set(user.Id, day, result.Select(i => i.Id).ToList());
            }

            return result.Take(count).Select(i => (i, reasons.GetValueOrDefault(i.Id) ?? string.Empty)).ToList();
        }

        /// <summary>
        /// Takes a title out of the banner for some days („Später“). It stays in the recommendations and does not count as rejection.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="itemId">The title.</param>
        /// <param name="days">Days (1–90).</param>
        /// <returns>False if the title is unknown or not visible.</returns>
        public bool Later(User user, Guid itemId, int days)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (_libraryManager.GetItemById(itemId) is not { } item || !item.IsVisibleStandalone(user))
            {
                return false;
            }

            _store.SetLater(user.Id, item.Id, DateTime.UtcNow.AddDays(Math.Clamp(days, 1, 90)));
            return true;
        }

        private static bool HasBackdrop(BaseItem item) => item.GetImages(ImageType.Backdrop).Any();

        private Dictionary<string, DateTime> ShownSince(Guid userId, DateTime now)
        {
            var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (_feedback == null)
            {
                return result;
            }

            foreach (var section in new[] { FeedbackStore.Movies, FeedbackStore.Series })
            {
                foreach (var (key, since) in _feedback.GetShownSince(userId, section, now))
                {
                    result[key] = since;
                }
            }

            return result;
        }

        // Angefangene Serien (zuletzt gesehene Folge in den letzten 60 Tagen, noch ungesehene Folgen) und angefangene
        // Filme, zuletzt Gesehenes zuerst
        private List<(BaseItem Item, string Reason)> ContinueCandidates(User user, DateTime now)
        {
            var result = new List<(BaseItem Item, string Reason)>();
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = 200
            });
            var lookup = UserDataLookup.Load(_userDataManager, user, episodes);
            var seriesIds = new List<Guid>();
            var lastPlayed = new Dictionary<Guid, DateTime>();
            foreach (var episode in episodes.OfType<Episode>())
            {
                if (episode.SeriesId == Guid.Empty || lastPlayed.ContainsKey(episode.SeriesId))
                {
                    continue;
                }

                if (lookup.Get(episode)?.LastPlayedDate is DateTime played && now - played <= ContinueWindow)
                {
                    lastPlayed[episode.SeriesId] = played;
                    seriesIds.Add(episode.SeriesId);
                }
            }

            if (seriesIds.Count > 0)
            {
                var counts = _libraryManager.GetPlayedAndTotalCountBatch(seriesIds, user);
                foreach (var seriesId in seriesIds.Take(15))
                {
                    if (counts == null || !counts.TryGetValue(seriesId, out var count) || count.Played >= count.Total)
                    {
                        continue;
                    }

                    if (_libraryManager.GetItemById(seriesId) is Series series && series.IsVisibleStandalone(user))
                    {
                        var newEpisodes = series.DateLastMediaAdded is DateTime added && added > lastPlayed[seriesId];
                        result.Add((series, newEpisodes ? ReasonNewEpisodes : ReasonContinueSeries));
                    }
                }
            }

            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsResumable = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = 10
            });
            result.AddRange(movies.Select(m => (m, ReasonContinueMovie)));
            return result;
        }

        private bool Usable(User user, BaseItem item)
        {
            return item.IsVisibleStandalone(user) && _userDataManager.GetUserData(user, item)?.Played != true;
        }
    }
}
