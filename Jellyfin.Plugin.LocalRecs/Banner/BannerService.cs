using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LocalRecs.Banner
{
    /// <summary>
    /// Der Banner des Tages je Benutzer, für alle Geräte gleich: gewichtet aus den besten Film- und Serien-Empfehlungen,
    /// am Tag fest, am nächsten Tag neu. Titel aus dem Banner der letzten Tage und lange unbeachtete Empfehlungen kommen
    /// seltener dran.
    /// </summary>
    public class BannerService
    {
        private readonly RecommendationSectionHandler _recommendations;
        private readonly BannerStore _store;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly FeedbackStore? _feedback;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerService"/> class.
        /// </summary>
        /// <param name="recommendations">Movie and series recommendations.</param>
        /// <param name="store">Banner of the last days.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="feedback">What was recommended since when, optional.</param>
        public BannerService(RecommendationSectionHandler recommendations, BannerStore store, ILibraryManager libraryManager, IUserDataManager userDataManager, FeedbackStore? feedback = null)
        {
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _feedback = feedback;
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
        /// Gets the banner of today for a user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone (for the day), optional.</param>
        /// <param name="count">Number of titles (1–10).</param>
        /// <returns>The titles.</returns>
        public IReadOnlyList<BaseItem> GetBanner(User user, int? utcOffsetMinutes, int count)
        {
            ArgumentNullException.ThrowIfNull(user);
            count = Math.Clamp(count, 1, 10);
            var now = DateTime.UtcNow;
            var day = DateOnly.FromDateTime(now.AddMinutes(BehaviorService.ResolveOffset(utcOffsetMinutes)));

            // Schon für heute gewählt: dieselben Titel, solange alle sichtbar und ungesehen sind
            // (bei wenigen Empfehlungen kann die Auswahl kleiner sein als gewünscht)
            var stored = _store.Get(user.Id, day);
            if (stored != null && stored.Count > 0)
            {
                var items = stored.Select(id => _libraryManager.GetItemById(id)).OfType<BaseItem>().Where(i => Usable(user, i)).ToList();
                if (items.Count == stored.Count && (items.Count >= count || items.Count < BannerPicker.DefaultCount))
                {
                    return items.Take(count).ToList();
                }
            }

            var movies = _recommendations.GetItems(user.Id, RecommendationSectionHandler.MoviesSection, utcOffsetMinutes);
            var series = _recommendations.GetItems(user.Id, RecommendationSectionHandler.SeriesSection, utcOffsetMinutes);
            var candidates = BannerPicker.Interleave(movies.Take(BannerPicker.MaxCandidates / 2).ToList(), series.Take(BannerPicker.MaxCandidates / 2).ToList());

            // Ohne Hintergrundbild taugt ein Titel nicht für den Banner – nur wenn sonst zu wenige bleiben
            var withBackdrop = candidates.Where(i => i.GetImages(ImageType.Backdrop).Any()).ToList();
            var pool = withBackdrop.Count >= count ? withBackdrop : candidates;

            var recent = _store.RecentlyShown(user.Id, day);
            var shownSince = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (_feedback != null)
            {
                foreach (var section in new[] { FeedbackStore.Movies, FeedbackStore.Series })
                {
                    foreach (var (key, since) in _feedback.GetShownSince(user.Id, section, now))
                    {
                        shownSince[key] = since;
                    }
                }
            }

            var picked = BannerPicker.Pick(
                pool,
                item => RecentFactor(recent.TryGetValue(item.Id, out var ago) ? ago : null)
                    * IgnoredFactor(shownSince.TryGetValue(item.Id.ToString("N"), out var since) ? (now - since).TotalDays : null),
                count,
                BannerPicker.Seed(user.Id, day));
            if (picked.Count > 0)
            {
                _store.Set(user.Id, day, picked.Select(i => i.Id).ToList());
            }

            return picked;
        }

        private bool Usable(User user, BaseItem item)
        {
            return item.IsVisibleStandalone(user) && _userDataManager.GetUserData(user, item)?.Played != true;
        }
    }
}
