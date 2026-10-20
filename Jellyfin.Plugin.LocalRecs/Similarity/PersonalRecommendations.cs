using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// Personal recommendations of a user at Trakt (TMDB IDs, best first).
    /// </summary>
    public class PersonalRecommendations
    {
        /// <summary>Gets or sets the recommended movies.</summary>
        public IReadOnlyList<string> Movies { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the recommended shows.</summary>
        public IReadOnlyList<string> Shows { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the access used ("eigen" or "trakt-plugin").</summary>
        public string Source { get; set; } = string.Empty;
    }
}
