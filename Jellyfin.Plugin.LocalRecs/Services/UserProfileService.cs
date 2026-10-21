using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for building user taste profiles from watch history.
    /// Aggregates weighted embeddings into normalized taste vectors.
    /// </summary>
    public class UserProfileService
    {
        /// <summary>
        /// How strongly a "Gefällt mir nicht" counts against similar items (relative to a watched item).
        /// </summary>
        public const float DislikeFactor = 0.5f;

        /// <summary>
        /// How strongly an abandoned movie (stopped early, not resumed) pulls similar titles down.
        /// </summary>
        public const float AbandonFactor = 0.25f;

        /// <summary>
        /// How strongly a "Gefällt mir" on a never played title counts (relative to a watched item, without favorite boost).
        /// </summary>
        public const float UnplayedLikeFactor = 0.3f;

        /// <summary>
        /// Share of the runtime below which a stopped movie counts as abandoned.
        /// </summary>
        public const double AbandonShare = 0.25;

        /// <summary>
        /// Days after the last playback before a stopped movie counts as abandoned (it may still be resumed).
        /// </summary>
        public const int AbandonAfterDays = 3;

        /// <summary>
        /// Share of the runtime from which Jellyfin marks a movie as played.
        /// </summary>
        public const double PlayedShare = 0.9;

        // Höchstens so viele gesehene Titel kommen als Begründung („Weil du … geschaut hast“) in Frage
        private const int MaxReasonSources = 300;

        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<UserProfileService> _logger;
        private readonly LiveTvWatchStore? _liveTvWatchStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfileService"/> class.
        /// </summary>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="liveTvWatchStore">Optional live TV watch history.</param>
        public UserProfileService(
            IUserDataManager userDataManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<UserProfileService> logger,
            LiveTvWatchStore? liveTvWatchStore = null)
        {
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _liveTvWatchStore = liveTvWatchStore;
        }

        /// <summary>
        /// Checks whether a movie was stopped early and not resumed for a few days.
        /// </summary>
        /// <param name="positionTicks">Playback position.</param>
        /// <param name="runTimeTicks">Runtime of the movie.</param>
        /// <param name="lastPlayed">Last playback (UTC).</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>True if the movie counts as abandoned.</returns>
        public static bool IsAbandoned(long positionTicks, long? runTimeTicks, DateTime? lastPlayed, DateTime now)
        {
            return positionTicks > 0
                && runTimeTicks > 0
                && positionTicks < runTimeTicks.Value * AbandonShare
                && lastPlayed != null
                && now - lastPlayed.Value >= TimeSpan.FromDays(AbandonAfterDays);
        }

        /// <summary>
        /// Weight of a watched movie by how often it was watched: once 1, twice 1.25, up to 1.75 from four times.
        /// </summary>
        /// <param name="playCount">Play count.</param>
        /// <returns>The factor.</returns>
        public static float RewatchFactor(int playCount) => 1f + (0.25f * (Math.Clamp(playCount, 1, 4) - 1));

        /// <summary>
        /// Weight of a started, not finished movie (at least a quarter watched): 0.43 at a quarter up to 0.75 shortly before the end.
        /// </summary>
        /// <param name="positionTicks">Playback position.</param>
        /// <param name="runTimeTicks">Runtime.</param>
        /// <returns>The factor, or 0 if it does not count (too little watched, no runtime, finished).</returns>
        public static float ProgressFactor(long positionTicks, long? runTimeTicks)
        {
            if (positionTicks <= 0 || runTimeTicks is not > 0)
            {
                return 0f;
            }

            var share = (double)positionTicks / runTimeTicks.Value;
            return share < AbandonShare || share >= PlayedShare ? 0f : (float)(0.3 + (0.5 * share));
        }

        /// <summary>
        /// Weight of a series by the share of watched episodes: 0.6 for barely started up to 1.4 for watched completely.
        /// </summary>
        /// <param name="played">Watched episodes.</param>
        /// <param name="total">All episodes.</param>
        /// <returns>The factor (1 if unknown).</returns>
        public static float SeriesFactor(int played, int total)
        {
            if (total <= 0 || played <= 0)
            {
                return 1f;
            }

            return 0.6f + (0.8f * Math.Min(1f, (float)played / total));
        }

        /// <summary>
        /// Builds a user profile from watch history and embeddings.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="embeddings">Dictionary of item embeddings.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>UserProfile with taste vector, or null if user has no watch history.</returns>
        /// <exception cref="ArgumentNullException">Thrown when embeddings or config is null.</exception>
        /// <exception cref="ArgumentException">Thrown when embeddings dictionary is empty.</exception>
        public UserProfile? BuildUserProfile(
            Guid userId,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            PluginConfiguration config)
        {
            if (embeddings == null)
            {
                throw new ArgumentNullException(nameof(embeddings));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (embeddings.Count == 0)
            {
                throw new ArgumentException("Embeddings dictionary cannot be empty", nameof(embeddings));
            }

            _logger.LogDebug("Building user profile for user {UserId}", userId);

            // Get watch records for this user
            var watchRecords = GetWatchRecords(userId, embeddings.Keys, config);

            if (watchRecords.Count == 0)
            {
                _logger.LogWarning("No watch history found for user {UserId}", userId);
                return null; // Return null to trigger cold-start recommendations
            }

            _logger.LogDebug("Found {Count} watched items for user {UserId}", watchRecords.Count, userId);

            // Compute weighted taste vector
            var tasteVector = ComputeTasteVector(watchRecords, embeddings, config);

            // Compute rating statistics from watched items
            var (avgCommunity, avgCritic, communityStdDev, criticStdDev) = ComputeRatingStatistics(watchRecords.Where(r => !r.IsAbandoned).ToList());

            var profile = new UserProfile(userId, tasteVector)
            {
                WatchedItemCount = watchRecords.Count(r => !r.IsAbandoned && !r.IsUnplayedLike),
                AverageCommunityRating = avgCommunity,
                AverageCriticRating = avgCritic,
                CommunityRatingStdDev = communityStdDev,
                CriticRatingStdDev = criticStdDev,
                PositiveItemIds = watchRecords
                    .Where(r => r.Likes != false && !r.IsAbandoned && !r.IsUnplayedLike)
                    .OrderByDescending(r => r.LastPlayedDate)
                    .Select(r => r.ItemId)
                    .Take(MaxReasonSources)
                    .ToList()
            };

            _logger.LogDebug(
                "Built profile for user {UserId}: {Count} items, AvgCommunity={AvgCommunity:F2}, AvgCritic={AvgCritic:F2}",
                userId,
                watchRecords.Count,
                avgCommunity ?? 0,
                avgCritic ?? 0);

            return profile;
        }

        /// <summary>
        /// Gets watch records for a user.
        /// For movies, includes items that have been fully watched (Played = true).
        /// For series, includes items with any watched episodes to capture partial engagement.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="availableItemIds">Available item IDs (from embeddings).</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>List of watch records.</returns>
        private List<WatchRecord> GetWatchRecords(Guid userId, IEnumerable<Guid> availableItemIds, PluginConfiguration config)
        {
            var records = new List<WatchRecord>();
            var user = _userManager.GetUserById(userId);

            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", userId);
                return records;
            }

            var itemIdSet = availableItemIds.ToHashSet();
            var itemsByTitle = new Dictionary<string, Guid>();
            var seriesRecords = new Dictionary<Guid, WatchRecord>();
            var userDataLookup = UserDataLookup.Load(_userDataManager, user, itemIdSet.Select(id => _libraryManager.GetItemById(id)).OfType<BaseItem>());

            foreach (var itemId in itemIdSet)
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    continue;
                }

                itemsByTitle.TryAdd(LiveTvText.NormalizeTitle(item.Name), itemId);

                var userData = userDataLookup.Get(item);

                if (userData == null)
                {
                    continue;
                }

                var unplayedLike = false;

                // For series, userData.Played is unreliable — it only becomes true when ALL
                // episodes are watched. Use the most recently watched episode's date instead.
                DateTime lastPlayedDate;
                if (item is Series series)
                {
                    var seriesLastPlayed = GetLastWatchedEpisodeDate(series, user);
                    if (seriesLastPlayed == null)
                    {
                        // Bewertete, aber (noch) nicht gesehene Serien zählen trotzdem (Daumen hoch/runter)
                        if (userData.Likes == null)
                        {
                            continue;
                        }

                        seriesLastPlayed = userData.LastPlayedDate ?? DateTime.UtcNow;
                        unplayedLike = userData.Likes == true;
                    }

                    lastPlayedDate = seriesLastPlayed.Value;
                }
                else if (!userData.Played && userData.Likes == null)
                {
                    // Früh abgebrochene Filme zählen als leises „Gefällt mir nicht“
                    if (IsAbandoned(userData.PlaybackPositionTicks, item.RunTimeTicks, userData.LastPlayedDate, DateTime.UtcNow))
                    {
                        records.Add(new WatchRecord(itemId, userId, userData.LastPlayedDate!.Value)
                        {
                            IsAbandoned = true,
                            CommunityRating = item.CommunityRating,
                            CriticRating = item.CriticRating
                        });
                    }
                    else if (ProgressFactor(userData.PlaybackPositionTicks, item.RunTimeTicks) is var progress and > 0f)
                    {
                        // Angefangen und mindestens ein Viertel geschaut: schwaches Interesse
                        records.Add(new WatchRecord(itemId, userId, userData.LastPlayedDate ?? DateTime.UtcNow)
                        {
                            Engagement = progress,
                            CommunityRating = item.CommunityRating,
                            CriticRating = item.CriticRating
                        });
                    }

                    // For movies, Played = true means the movie was completed.
                    continue;
                }
                else
                {
                    lastPlayedDate = userData.LastPlayedDate ?? DateTime.UtcNow;
                    unplayedLike = !userData.Played && userData.Likes == true && userData.PlaybackPositionTicks <= 0;
                }

                // Nie gespielte Titel mit „Gefällt mir“ (z. B. Merkliste von Jellyfin Enhanced) nur schwach werten
                var record = new WatchRecord(itemId, userId, lastPlayedDate)
                {
                    IsFavorite = userData.IsFavorite || (userData.Likes == true && !unplayedLike),
                    IsUnplayedLike = unplayedLike,
                    Likes = userData.Likes,
                    CommunityRating = item.CommunityRating,
                    CriticRating = item.CriticRating,
                    Engagement = item is Series || !userData.Played ? 1f : RewatchFactor(userData.PlayCount)
                };

                records.Add(record);
                if (item is Series && !unplayedLike)
                {
                    seriesRecords[itemId] = record;
                }
            }

            // Serien: Anteil gesehener Folgen in einer Abfrage für alle
            if (seriesRecords.Count > 0)
            {
                try
                {
                    var counts = _libraryManager.GetPlayedAndTotalCountBatch(seriesRecords.Keys.ToList(), user);
                    foreach (var (seriesId, record) in seriesRecords)
                    {
                        if (counts != null && counts.TryGetValue(seriesId, out var count))
                        {
                            record.Engagement = SeriesFactor(count.Played, count.Total);
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException)
                {
                    _logger.LogDebug(ex, "Anteil gesehener Folgen nicht ermittelbar");
                }
            }

            AddLiveTvWatchRecords(userId, records, itemsByTitle, config);

            return records;
        }

        /// <summary>
        /// Adds library items whose title was watched on live TV long enough (e.g. a series running on TV
        /// that is also in the library) as watch records.
        /// </summary>
        private void AddLiveTvWatchRecords(
            Guid userId,
            List<WatchRecord> records,
            Dictionary<string, Guid> itemsByTitle,
            PluginConfiguration config)
        {
            if (_liveTvWatchStore == null || !config.EnableLiveTvTracking)
            {
                return;
            }

            var excluded = LiveTvText.ParseList(config.LiveTvExcludedGenres);
            var known = records.Select(r => r.ItemId).ToHashSet();

            var titles = _liveTvWatchStore.GetEntries(userId, config.LiveTvMinMinutes)
                .Where(e => !LiveTvText.IsExcluded(e.Genres, excluded))
                .GroupBy(LiveTvText.TitleKey)
                .Where(g => g.Key.Length > 0 && g.Sum(e => e.Minutes) >= config.LiveTvTitleMatchMinutes);

            foreach (var title in titles)
            {
                if (itemsByTitle.TryGetValue(title.Key, out var itemId) && known.Add(itemId))
                {
                    records.Add(new WatchRecord(itemId, userId, title.Max(e => e.LastWatched)));
                    _logger.LogDebug("Live-TV title {Title} counts as watched library item {ItemId}", title.Key, itemId);
                }
            }
        }

        /// <summary>
        /// Returns the most recent LastPlayedDate across watched episodes of a series,
        /// or null if no episodes have been watched.
        /// </summary>
        private DateTime? GetLastWatchedEpisodeDate(Series series, Jellyfin.Database.Implementations.Entities.User user)
        {
            var result = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                AncestorIds = new[] { series.Id },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                Limit = 1
            });

            if (result.Count == 0)
            {
                return null;
            }

            var epData = _userDataManager.GetUserData(user, result[0]);
            return epData?.LastPlayedDate ?? DateTime.UtcNow;
        }

        /// <summary>
        /// Computes the user's taste vector as a weighted sum of watched item embeddings.
        /// </summary>
        /// <param name="watchRecords">User's watch records.</param>
        /// <param name="embeddings">Item embeddings.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Normalized taste vector.</returns>
        private float[] ComputeTasteVector(
            List<WatchRecord> watchRecords,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            PluginConfiguration config)
        {
            // Get dimension from first embedding
            var firstEmbedding = embeddings.Values.FirstOrDefault();
            if (firstEmbedding == null)
            {
                throw new InvalidOperationException("No embeddings available");
            }

            var dimension = firstEmbedding.Dimensions;
            var weightedSum = new float[dimension];
            float totalWeight = 0;

            var now = DateTime.UtcNow;

            // Accumulate weighted embeddings for each watched item
            foreach (var record in watchRecords)
            {
                if (!embeddings.TryGetValue(record.ItemId, out var embedding))
                {
                    continue; // Skip items without embeddings
                }

                // Compute combined weight: recency decay × favorite boost × rewatch boost
                var daysSince = (now - record.LastPlayedDate).TotalDays;
                var weight = WeightCalculator.ComputeCombinedWeight(
                    daysSince,
                    config.RecencyDecayHalfLifeDays,
                    record.IsFavorite,
                    (float)config.FavoriteBoost,
                    (float)config.RecentWatchBoost);
                if (record.IsUnplayedLike)
                {
                    weight *= UnplayedLikeFactor;
                }

                // Mehrfach gesehen, Serie weit geschaut bzw. Film nur angefangen
                weight *= record.Engagement;

                // Daumen runter bzw. früh abgebrochen: vom Geschmack abziehen (ähnliche Titel werden seltener empfohlen)
                if (record.Likes == false || record.IsAbandoned)
                {
                    var factor = record.Likes == false ? DislikeFactor : AbandonFactor;
                    for (int i = 0; i < dimension; i++)
                    {
                        weightedSum[i] -= embedding.Vector[i] * weight * factor;
                    }

                    continue;
                }

                // Accumulate weighted vectors
                for (int i = 0; i < dimension; i++)
                {
                    weightedSum[i] += embedding.Vector[i] * weight;
                }

                totalWeight += weight;
            }

            // Normalize by total weight to get weighted average, then normalize to unit length
            if (totalWeight > 0)
            {
                for (int i = 0; i < dimension; i++)
                {
                    weightedSum[i] /= totalWeight;
                }
            }

            return VectorMath.Normalize(weightedSum);
        }

        /// <summary>
        /// Computes rating statistics from watched items.
        /// Uses ratings cached in WatchRecord to avoid duplicate library lookups.
        /// </summary>
        /// <param name="watchRecords">User's watch records (with ratings already populated).</param>
        /// <returns>Rating statistics with averages and standard deviations.</returns>
        private (float? AvgCommunityRating, float? AvgCriticRating, float CommunityStdDev, float CriticStdDev) ComputeRatingStatistics(
            List<WatchRecord> watchRecords)
        {
            // Extract ratings from watch records (already populated during GetWatchRecords)
            var communityRatings = watchRecords
                .Where(r => r.CommunityRating.HasValue)
                .Select(r => r.CommunityRating!.Value)
                .ToList();

            var criticRatings = watchRecords
                .Where(r => r.CriticRating.HasValue)
                .Select(r => r.CriticRating!.Value)
                .ToList();

            // Compute community rating statistics
            float? avgCommunity = null;
            float communityStdDev = 0f;
            if (communityRatings.Any())
            {
                avgCommunity = communityRatings.Average();
                if (communityRatings.Count > 1)
                {
                    var variance = communityRatings.Average(r => Math.Pow(r - avgCommunity.Value, 2));
                    communityStdDev = (float)Math.Sqrt(variance);
                }
            }

            // Compute critic rating statistics
            float? avgCritic = null;
            float criticStdDev = 0f;
            if (criticRatings.Any())
            {
                avgCritic = criticRatings.Average();
                if (criticRatings.Count > 1)
                {
                    var variance = criticRatings.Average(r => Math.Pow(r - avgCritic.Value, 2));
                    criticStdDev = (float)Math.Sqrt(variance);
                }
            }

            return (avgCommunity, avgCritic, communityStdDev, criticStdDev);
        }
    }
}
