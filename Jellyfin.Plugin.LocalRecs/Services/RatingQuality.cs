using System;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Berücksichtigt die Community-Bewertung: schwach bewertete Titel werden aussortiert,
    /// gut bewertete rücken bei ähnlicher Passung nach vorne.
    /// </summary>
    public static class RatingQuality
    {
        /// <summary>
        /// Checks whether an item's community rating is below the configured minimum.
        /// Items without a rating are never filtered.
        /// </summary>
        /// <param name="itemMetadata">The item's metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>True if the item is rated too low.</returns>
        public static bool IsBelowMinimum(MediaItemMetadata itemMetadata, PluginConfiguration config)
        {
            return config.MinCommunityRating > 0
                && itemMetadata.CommunityRating.HasValue
                && itemMetadata.CommunityRating.Value > 0
                && itemMetadata.CommunityRating.Value < config.MinCommunityRating;
        }

        /// <summary>
        /// Blends the score with the item's community rating, so that well rated titles rank higher
        /// among similarly matching ones. Items without a rating count as neutral (5/10).
        /// </summary>
        /// <param name="score">Score so far.</param>
        /// <param name="itemMetadata">The item's metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>The blended score.</returns>
        public static double Apply(double score, MediaItemMetadata itemMetadata, PluginConfiguration config)
        {
            var weight = Math.Clamp(config.RatingQualityWeight, 0, 1);
            if (weight <= 0)
            {
                return score;
            }

            var rating = itemMetadata.CommunityRating.HasValue && itemMetadata.CommunityRating.Value > 0
                ? Math.Clamp(itemMetadata.CommunityRating.Value / 10.0, 0, 1)
                : 0.5;
            return ((1 - weight) * score) + (weight * rating);
        }
    }
}
