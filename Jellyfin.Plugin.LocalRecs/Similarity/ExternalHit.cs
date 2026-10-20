using System;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// How strongly public sources name a library item as similar to the user's titles.
    /// </summary>
    public class ExternalHit
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalHit"/> class.
        /// </summary>
        /// <param name="strength">Strength between 0 and 1.</param>
        /// <param name="seedId">The user's title that contributed most.</param>
        public ExternalHit(double strength, Guid seedId)
        {
            Strength = strength;
            SeedId = seedId;
        }

        /// <summary>Gets the strength between 0 and 1.</summary>
        public double Strength { get; }

        /// <summary>Gets the user's title that contributed most.</summary>
        public Guid SeedId { get; }
    }
}
