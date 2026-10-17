using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Feinschliff der Film- und Serienempfehlungen nach der Bewertung:
    /// <list type="bullet">
    /// <item>Titel, die schon lange in der Zeile stehen und nie geschaut wurden, rutschen nach und nach nach hinten.</item>
    /// <item>Höchstens ein Drittel der Zeile aus demselben Hauptgenre, der Rest wird aufgefüllt.</item>
    /// </list>
    /// </summary>
    public static class RecommendationReranker
    {
        /// <summary>Score factor per day after the grace period.</summary>
        public const float PenaltyPerDay = 0.02f;

        /// <summary>Lowest score factor for long ignored titles.</summary>
        public const float MinFactor = 0.5f;

        /// <summary>
        /// Score factor for a title that has been recommended for <paramref name="shownDays"/> days without being watched.
        /// </summary>
        /// <param name="shownDays">Days the title has been in the row.</param>
        /// <param name="graceDays">Days without penalty (0 = off).</param>
        /// <returns>Factor between <see cref="MinFactor"/> and 1.</returns>
        public static float IgnoredFactor(double shownDays, int graceDays)
        {
            if (graceDays <= 0 || shownDays <= graceDays)
            {
                return 1f;
            }

            return Math.Max(MinFactor, 1f - (PenaltyPerDay * (float)(shownDays - graceDays)));
        }

        /// <summary>
        /// Applies the penalty for ignored titles and the genre limit, and returns the final row.
        /// </summary>
        /// <param name="scored">Scored candidates (more than <paramref name="limit"/>, best first).</param>
        /// <param name="metadata">Item metadata (for the main genre).</param>
        /// <param name="shownSince">Since when each item has been recommended without being watched (UTC), by item ID.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <param name="graceDays">Days without penalty (0 = off).</param>
        /// <param name="limit">Length of the row.</param>
        /// <returns>The final recommendations.</returns>
        public static List<ScoredRecommendation> Rerank(
            IEnumerable<ScoredRecommendation> scored,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            IReadOnlyDictionary<Guid, DateTime>? shownSince,
            DateTime now,
            int graceDays,
            int limit)
        {
            ArgumentNullException.ThrowIfNull(scored);
            ArgumentNullException.ThrowIfNull(metadata);
            if (limit <= 0)
            {
                return new List<ScoredRecommendation>();
            }

            var adjusted = scored
                .Select(r =>
                {
                    if (shownSince == null || !shownSince.TryGetValue(r.ItemId, out var since))
                    {
                        return r;
                    }

                    var factor = IgnoredFactor((now - since).TotalDays, graceDays);
                    return factor >= 1f ? r : new ScoredRecommendation(r.ItemId, r.Score * factor);
                })
                .OrderByDescending(r => r.Score)
                .ToList();

            var cap = Math.Max(2, (int)Math.Ceiling(limit / 3.0));
            var perGenre = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var picked = new List<ScoredRecommendation>();
            var leftovers = new List<ScoredRecommendation>();
            foreach (var recommendation in adjusted)
            {
                if (picked.Count >= limit)
                {
                    break;
                }

                var genre = metadata.TryGetValue(recommendation.ItemId, out var item) && item.Genres.Count > 0
                    ? item.Genres[0]
                    : string.Empty;
                var count = perGenre.GetValueOrDefault(genre);
                if (genre.Length == 0 || count < cap)
                {
                    perGenre[genre] = count + 1;
                    picked.Add(recommendation);
                }
                else
                {
                    leftovers.Add(recommendation);
                }
            }

            // Reicht die Auswahl nicht, mit den besten übrigen auffüllen – die Reihenfolge bleibt nach Score
            picked.AddRange(leftovers.Take(limit - picked.Count));
            return picked.OrderByDescending(r => r.Score).ToList();
        }
    }
}
