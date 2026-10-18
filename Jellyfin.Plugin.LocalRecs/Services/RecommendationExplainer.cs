using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Utilities;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Begründet Empfehlungen mit dem ähnlichsten gesehenen Titel: „Weil du „Frieren“ geschaut hast“.
    /// </summary>
    public static class RecommendationExplainer
    {
        /// <summary>
        /// Minimum similarity between the recommended and the watched title for a reason.
        /// </summary>
        public const float MinSimilarity = 0.35f;

        /// <summary>
        /// Builds the reason text for a watched title.
        /// </summary>
        /// <param name="watchedTitle">Title of the watched item.</param>
        /// <returns>The reason.</returns>
        public static string BecauseYouWatched(string watchedTitle) => "Weil du „" + watchedTitle + "“ geschaut hast";

        /// <summary>
        /// Sets <see cref="ScoredRecommendation.Reason"/> for recommendations without a reason yet.
        /// </summary>
        /// <param name="recommendations">The recommendations.</param>
        /// <param name="watchedItemIds">Watched (and liked) items of the user.</param>
        /// <param name="embeddings">Item embeddings.</param>
        /// <param name="metadata">Item metadata (for titles).</param>
        public static void Explain(
            IEnumerable<ScoredRecommendation> recommendations,
            IReadOnlyList<Guid> watchedItemIds,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata)
        {
            ArgumentNullException.ThrowIfNull(recommendations);
            ArgumentNullException.ThrowIfNull(watchedItemIds);
            ArgumentNullException.ThrowIfNull(embeddings);
            ArgumentNullException.ThrowIfNull(metadata);

            var sources = watchedItemIds
                .Where(id => embeddings.ContainsKey(id) && metadata.ContainsKey(id))
                .Select(id => (Id: id, Vector: embeddings[id].Vector, Name: metadata[id].Name))
                .ToList();
            if (sources.Count == 0)
            {
                return;
            }

            foreach (var recommendation in recommendations.Where(r => r.Reason == null))
            {
                if (!embeddings.TryGetValue(recommendation.ItemId, out var embedding))
                {
                    continue;
                }

                var best = sources
                    .Where(s => s.Id != recommendation.ItemId)
                    .Select(s => (s.Name, Similarity: VectorMath.CosineSimilarity(embedding.Vector, s.Vector)))
                    .OrderByDescending(s => s.Similarity)
                    .FirstOrDefault();
                if (best.Name != null && best.Similarity >= MinSimilarity)
                {
                    recommendation.Reason = BecauseYouWatched(best.Name);
                }
            }
        }
    }
}
