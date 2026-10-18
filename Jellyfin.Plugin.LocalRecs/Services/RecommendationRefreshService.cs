using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for refreshing recommendations for users.
    /// Used by the scheduled task to generate recommendations for all users.
    /// No caching - computes fresh recommendations on every run.
    /// </summary>
    public class RecommendationRefreshService
    {
        // Länge der gespeicherten Rangliste je Welt
        private const int MaxWorldItems = 60;

        private readonly ILogger<RecommendationRefreshService> _logger;
        private readonly LibraryAnalysisService _libraryAnalysisService;
        private readonly VocabularyBuilder _vocabularyBuilder;
        private readonly EmbeddingService _embeddingService;
        private readonly UserProfileService _userProfileService;
        private readonly RecommendationEngine _recommendationEngine;
        private readonly FeedbackStore? _feedback;
        private readonly CollectionService? _collections;
        private readonly WorldService? _worlds;
        private readonly ConcurrentDictionary<Guid, WorldRankings> _worldRankings = new ConcurrentDictionary<Guid, WorldRankings>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationRefreshService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryAnalysisService">Library analysis service.</param>
        /// <param name="vocabularyBuilder">Vocabulary builder.</param>
        /// <param name="embeddingService">Embedding service.</param>
        /// <param name="userProfileService">User profile service.</param>
        /// <param name="recommendationEngine">Recommendation engine.</param>
        /// <param name="feedback">Evaluation store (since when titles have been recommended), optional.</param>
        /// <param name="collections">Film series (collections), optional.</param>
        /// <param name="worlds">Worlds (Marvel, Star Wars, …), optional.</param>
        public RecommendationRefreshService(
            ILogger<RecommendationRefreshService> logger,
            LibraryAnalysisService libraryAnalysisService,
            VocabularyBuilder vocabularyBuilder,
            EmbeddingService embeddingService,
            UserProfileService userProfileService,
            RecommendationEngine recommendationEngine,
            FeedbackStore? feedback = null,
            CollectionService? collections = null,
            WorldService? worlds = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryAnalysisService = libraryAnalysisService ?? throw new ArgumentNullException(nameof(libraryAnalysisService));
            _vocabularyBuilder = vocabularyBuilder ?? throw new ArgumentNullException(nameof(vocabularyBuilder));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _userProfileService = userProfileService ?? throw new ArgumentNullException(nameof(userProfileService));
            _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
            _feedback = feedback;
            _collections = collections;
            _worlds = worlds;
        }

        /// <summary>
        /// Computes fresh embeddings for the library.
        /// Always recomputes to ensure recommendations reflect current watch history.
        /// </summary>
        /// <returns>Tuple of embeddings dictionary and metadata dictionary.</returns>
        public (IReadOnlyDictionary<Guid, ItemEmbedding> Embeddings, IReadOnlyDictionary<Guid, MediaItemMetadata> Metadata) ComputeEmbeddings()
        {
            // Get library metadata (always fresh)
            var library = _libraryAnalysisService.GetAllMediaItems();
            var metadata = library.ToDictionary(m => m.Id);

            // Always compute fresh embeddings to reflect current watch history
            _logger.LogDebug("Computing fresh embeddings for {Count} items", library.Count);

            var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

            var vocabulary = _vocabularyBuilder.BuildVocabulary(
                library,
                config.MaxVocabularyActors,
                config.MaxVocabularyDirectors,
                config.MaxVocabularyTags);
            var embeddings = _embeddingService.ComputeEmbeddings(library, vocabulary);

            var embeddingsDict = embeddings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return (embeddingsDict, metadata);
        }

        /// <summary>
        /// Generates recommendations for a single user.
        /// Returns tuple of (movie recommendations, TV recommendations).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="embeddings">Pre-computed embeddings.</param>
        /// <param name="metadata">Item metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Tuple of movie and TV recommendations.</returns>
        public (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv) GenerateRecommendationsForUser(
            Guid userId,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config)
        {
            _logger.LogDebug("Generating recommendations for user {UserId}", userId);

            try
            {
                // Build user profile (returns null if no watch history)
                var profile = _userProfileService.BuildUserProfile(userId, embeddings, config);

                // Generate movie recommendations (will use cold-start if profile is null)
                var movieRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Movie,
                    CandidateCount(config.MovieRecommendationCount));

                // Generate TV recommendations (will use cold-start if profile is null)
                var tvRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Series,
                    CandidateCount(config.TvRecommendationCount));

                // Lange ignorierte Titel nach hinten, höchstens ein Drittel aus demselben Genre
                var now = DateTime.UtcNow;
                movieRecs = RecommendationReranker.Rerank(movieRecs, metadata, ShownSince(userId, FeedbackStore.Movies, now), now, config.IgnoredRecommendationDays, config.MovieRecommendationCount);
                tvRecs = RecommendationReranker.Rerank(tvRecs, metadata, ShownSince(userId, FeedbackStore.Series, now), now, config.IgnoredRecommendationDays, config.TvRecommendationCount);

                // Filmreihen: nächster ungesehener Teil nach vorne
                var nextParts = _collections != null && config.EnableCollectionNextParts
                    ? _collections.GetNextParts(userId)
                    : Array.Empty<(Guid ItemId, string Reason)>();
                if (nextParts.Count > 0)
                {
                    movieRecs = RecommendationReranker.AddNextParts(
                        movieRecs,
                        nextParts,
                        id => !metadata.TryGetValue(id, out var item) || !RatingQuality.IsBelowMinimum(item, config),
                        config.MovieRecommendationCount);
                }

                // Begründung: ähnlichster gesehener Titel
                if (profile != null)
                {
                    RecommendationExplainer.Explain(movieRecs.Concat(tvRecs), profile.PositiveItemIds, embeddings, metadata);
                }

                // Welten: eigene Rangliste je Welt (Marvel, Star Wars, …)
                _worldRankings[userId] = RankWorlds(profile, nextParts, embeddings, metadata, config);

                _logger.LogDebug(
                    "Generated recommendations for user {UserId}: {MovieCount} movies, {TvCount} TV",
                    userId,
                    movieRecs.Count,
                    tvRecs.Count);

                return (movieRecs, tvRecs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate recommendations for user {UserId}", userId);
                return (new List<ScoredRecommendation>(), new List<ScoredRecommendation>());
            }
        }

        /// <summary>
        /// Generates recommendations for multiple users efficiently.
        /// Computes embeddings once and reuses them for all users.
        /// </summary>
        /// <param name="userIds">List of user IDs to process.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Dictionary mapping user IDs to their recommendations (movies, TV).</returns>
        public Task<Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>> GenerateRecommendationsForMultipleUsersAsync(
            IReadOnlyList<Guid> userIds,
            PluginConfiguration config)
        {
            var results = new Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>();

            if (userIds.Count == 0)
            {
                return Task.FromResult(results);
            }

            _logger.LogInformation("Generating recommendations for {Count} users", userIds.Count);

            // Compute embeddings once for all users
            var (embeddings, metadata) = ComputeEmbeddings();

            foreach (var userId in userIds)
            {
                var recs = GenerateRecommendationsForUser(userId, embeddings, metadata, config);
                results[userId] = recs;
            }

            _logger.LogInformation("Successfully generated recommendations for {Count}/{Total} users", results.Count, userIds.Count);

            return Task.FromResult(results);
        }

        /// <summary>
        /// Gets the world rankings computed for a user during the last run.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns>The rankings, or null.</returns>
        public WorldRankings? GetWorldRankings(Guid userId)
        {
            return _worldRankings.TryGetValue(userId, out var rankings) ? rankings : null;
        }

        // Mehr Kandidaten holen, damit nach Genre-Grenze und Abwertung die Zeile voll wird
        private static int CandidateCount(int limit) => limit <= 0 ? 0 : (limit * 3) + 10;

        private WorldRankings RankWorlds(
            UserProfile? profile,
            IReadOnlyList<(Guid ItemId, string Reason)> nextParts,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config)
        {
            var result = new WorldRankings();
            if (_worlds == null)
            {
                return result;
            }

            var (worlds, members) = _worlds.GetWorlds();
            foreach (var world in worlds)
            {
                var worldMembers = members[world.Id];
                var scored = worldMembers
                    .Where(id => embeddings.ContainsKey(id) && metadata.ContainsKey(id) && !RatingQuality.IsBelowMinimum(metadata[id], config))
                    .Select(id => new ScoredRecommendation(
                        id,
                        profile != null
                            ? (float)RatingQuality.Apply(VectorMath.CosineSimilarity(profile.TasteVector, embeddings[id].Vector), metadata[id], config)
                            : (metadata[id].CommunityRating ?? 0) / 10f))
                    .OrderByDescending(r => r.Score)
                    .ToList();

                scored = RecommendationReranker.AddNextParts(scored, nextParts.Where(p => worldMembers.Contains(p.ItemId)), _ => true, MaxWorldItems);
                if (profile != null)
                {
                    RecommendationExplainer.Explain(scored, profile.PositiveItemIds, embeddings, metadata);
                }

                result.Items[world.Id] = scored.Select(r => r.ItemId).ToList();
                foreach (var recommendation in scored.Where(r => r.Reason != null))
                {
                    result.Reasons.TryAdd(recommendation.ItemId, recommendation.Reason!);
                }
            }

            return result;
        }

        private Dictionary<Guid, DateTime>? ShownSince(Guid userId, string section, DateTime now)
        {
            if (_feedback == null)
            {
                return null;
            }

            return _feedback.GetShownSince(userId, section, now)
                .Where(kv => Guid.TryParse(kv.Key, out _))
                .ToDictionary(kv => Guid.Parse(kv.Key), kv => kv.Value);
        }
    }
}
