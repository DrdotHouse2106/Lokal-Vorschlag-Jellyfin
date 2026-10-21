using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Similarity;
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
        private readonly ExternalSimilarityService? _external;
        private readonly TraktPersonalService? _traktPersonal;
        private readonly ConcurrentDictionary<Guid, WorldRankings> _worldRankings = new ConcurrentDictionary<Guid, WorldRankings>();
        private IReadOnlyList<Guid> _lastSeeds = Array.Empty<Guid>();
        private IReadOnlyDictionary<Guid, MediaItemMetadata>? _lastMetadata;

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
        /// <param name="external">Public similarity lists (TMDB, Trakt), optional.</param>
        /// <param name="traktPersonal">Personal Trakt recommendations, optional.</param>
        public RecommendationRefreshService(
            ILogger<RecommendationRefreshService> logger,
            LibraryAnalysisService libraryAnalysisService,
            VocabularyBuilder vocabularyBuilder,
            EmbeddingService embeddingService,
            UserProfileService userProfileService,
            RecommendationEngine recommendationEngine,
            FeedbackStore? feedback = null,
            CollectionService? collections = null,
            WorldService? worlds = null,
            ExternalSimilarityService? external = null,
            TraktPersonalService? traktPersonal = null)
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
            _external = external;
            _traktPersonal = traktPersonal;
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
            UserProfile? profile;
            try
            {
                // Build user profile (returns null if no watch history)
                profile = _userProfileService.BuildUserProfile(userId, embeddings, config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build the profile of user {UserId}", userId);
                return (new List<ScoredRecommendation>(), new List<ScoredRecommendation>());
            }

            return GenerateForProfile(userId, profile, null, embeddings, metadata, config);
        }

        /// <summary>
        /// Hits of the public similarity lists for a user (titles named as similar to the user's watched or liked titles).
        /// </summary>
        /// <param name="profile">The user's profile, or null.</param>
        /// <param name="metadata">Item metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>The hits, or null if off or unknown.</returns>
        public IReadOnlyDictionary<Guid, ExternalHit>? ExternalHits(UserProfile? profile, IReadOnlyDictionary<Guid, MediaItemMetadata> metadata, PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(config);
            if (_external == null || !config.EnableExternalSimilarity || config.ExternalSimilarityWeight <= 0 || profile == null || profile.PositiveItemIds.Count == 0)
            {
                return null;
            }

            return ExternalSimilarityScorer.Compute(profile.PositiveItemIds.Select(id => (id, 1.0)), metadata, _external.Lookup);
        }

        /// <summary>
        /// Generates recommendations for multiple users efficiently.
        /// Computes embeddings once and reuses them for all users.
        /// </summary>
        /// <param name="userIds">List of user IDs to process.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token (stops fetching public similarity lists).</param>
        /// <returns>Dictionary mapping user IDs to their recommendations (movies, TV).</returns>
        public async Task<Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>> GenerateRecommendationsForMultipleUsersAsync(
            IReadOnlyList<Guid> userIds,
            PluginConfiguration config,
            CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>();

            if (userIds.Count == 0)
            {
                return results;
            }

            _logger.LogInformation("Generating recommendations for {Count} users", userIds.Count);

            // Compute embeddings once for all users
            var (embeddings, metadata) = ComputeEmbeddings();

            var profiles = new Dictionary<Guid, UserProfile?>();
            foreach (var userId in userIds)
            {
                try
                {
                    profiles[userId] = _userProfileService.BuildUserProfile(userId, embeddings, config);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build the profile of user {UserId}", userId);
                    profiles[userId] = null;
                }
            }

            // Für den Abgleich mit TMDB/Trakt nach dem Speichern (RefreshExternalAfterRunAsync)
            _lastSeeds = profiles.Values.Where(p => p != null).SelectMany(p => p!.PositiveItemIds).Distinct().ToList();
            _lastMetadata = metadata;

            foreach (var userId in userIds)
            {
                PersonalRecommendations? personal = null;
                if (_traktPersonal != null)
                {
                    try
                    {
                        personal = await _traktPersonal.GetRecommendationsAsync(userId, config, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug(ex, "Trakt-Empfehlungen für Benutzer {UserId} nicht verfügbar", userId);
                    }
                }

                results[userId] = GenerateForProfile(userId, profiles[userId], personal, embeddings, metadata, config);
            }

            _logger.LogInformation("Successfully generated recommendations for {Count}/{Total} users", results.Count, userIds.Count);

            return results;
        }

        /// <summary>
        /// Fetches missing or outdated public similarity lists (TMDB/Trakt) for the library of the last run.
        /// Runs after the recommendations are stored, so a long first fetch never delays them; the next run uses the new lists.
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public Task RefreshExternalAfterRunAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            var metadata = _lastMetadata;
            return metadata == null ? Task.CompletedTask : RefreshExternalAsync(_lastSeeds, metadata, config, cancellationToken);
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

            float WorldScore(Guid id) => profile != null
                ? (float)RatingQuality.Apply(VectorMath.CosineSimilarity(profile.TasteVector, embeddings[id].Vector), metadata[id], config)
                : (metadata[id].CommunityRating ?? 0) / 10f;

            var (worlds, members) = _worlds.GetWorlds();
            foreach (var world in worlds)
            {
                var worldMembers = members[world.Id];
                var scored = worldMembers
                    .Where(id => embeddings.ContainsKey(id) && metadata.ContainsKey(id) && !RatingQuality.IsBelowMinimum(metadata[id], config))
                    .Select(id => new ScoredRecommendation(id, WorldScore(id)))
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

        private (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv) GenerateForProfile(
            Guid userId,
            UserProfile? profile,
            PersonalRecommendations? personal,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config)
        {
            try
            {
                // Signale von außen: von TMDB/Trakt als ähnlich genannt, persönlich bei Trakt empfohlen
                var external = ExternalHits(profile, metadata, config);
                var personalHits = personal != null && config.EnableTraktPersonal && config.TraktPersonalWeight > 0
                    ? ExternalSimilarityScorer.PersonalHits(personal, metadata)
                    : null;
                var bonus = new Dictionary<Guid, double>();
                foreach (var hit in external ?? new Dictionary<Guid, ExternalHit>())
                {
                    bonus[hit.Key] = config.ExternalSimilarityWeight * hit.Value.Strength;
                }

                foreach (var hit in personalHits ?? new Dictionary<Guid, double>())
                {
                    bonus[hit.Key] = bonus.GetValueOrDefault(hit.Key) + (config.TraktPersonalWeight * hit.Value);
                }

                // Generate movie recommendations (will use cold-start if profile is null)
                var movieRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Movie,
                    CandidateCount(config.MovieRecommendationCount),
                    bonus);

                // Generate TV recommendations (will use cold-start if profile is null)
                var tvRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Series,
                    CandidateCount(config.TvRecommendationCount),
                    bonus);

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

                // Begründung: persönlich bei Trakt empfohlen, von TMDB/Trakt als ähnlich genannt, sonst ähnlichster gesehener Titel
                foreach (var recommendation in movieRecs.Concat(tvRecs).Where(r => r.Reason == null))
                {
                    if (personalHits != null && personalHits.ContainsKey(recommendation.ItemId))
                    {
                        recommendation.Reason = ExternalSimilarityScorer.TraktReason;
                    }
                    else if (external != null && external.TryGetValue(recommendation.ItemId, out var hit) && hit.Strength >= ExternalSimilarityScorer.ReasonThreshold
                        && metadata.TryGetValue(hit.SeedId, out var seed))
                    {
                        recommendation.Reason = ExternalSimilarityScorer.SimilarTo(seed.Name);
                    }
                }

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

        // Öffentliche Ähnlichkeitslisten auffrischen: gesehene/gemochte Titel zuerst, dann der übrige Bestand
        private async Task RefreshExternalAsync(
            IReadOnlyList<Guid> seedIds,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config,
            CancellationToken cancellationToken)
        {
            if (_external == null || !config.EnableExternalSimilarity || config.ExternalSimilarityWeight <= 0)
            {
                return;
            }

            var seeds = seedIds.Where(metadata.ContainsKey).Distinct().ToList();
            var seedSet = seeds.ToHashSet();
            var ordered = seeds.Select(id => metadata[id])
                .Concat(metadata.Values.Where(m => !seedSet.Contains(m.Id)).OrderBy(m => m.Id));
            try
            {
                var result = await _external.RefreshAsync(ordered, config, cancellationToken).ConfigureAwait(false);
                if (result.Skipped != null)
                {
                    _logger.LogDebug("Ähnliche Titel (TMDB/Trakt) übersprungen: {Reason}", result.Skipped);
                }
                else if (result.Aborted)
                {
                    _logger.LogInformation(
                        "Ähnliche Titel (TMDB/Trakt): {Fetched} Titel geholt, nach {Errors} Fehlern abgebrochen – {Pending} offen für die nächsten Läufe",
                        result.Fetched,
                        result.Errors,
                        result.Pending);
                }
                else if (result.Requests > 0)
                {
                    _logger.LogInformation(
                        "Ähnliche Titel (TMDB/Trakt): {Fetched} Titel geholt, {Pending} offen für die nächsten Läufe, {Errors} Fehler",
                        result.Fetched,
                        result.Pending,
                        result.Errors);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Die eigenen Empfehlungen dürfen daran nie scheitern
                _logger.LogWarning(ex, "Ähnliche Titel (TMDB/Trakt) konnten nicht aufgefrischt werden");
            }
        }
    }
}
