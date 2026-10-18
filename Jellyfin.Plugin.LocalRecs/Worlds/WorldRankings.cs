using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Personal ranking of the titles of each world for one user.
    /// </summary>
    public class WorldRankings
    {
        /// <summary>
        /// Gets or sets the ranked item IDs by world ID (best first, watched titles are filtered when shown).
        /// </summary>
        public Dictionary<string, List<Guid>> Items { get; set; } = new Dictionary<string, List<Guid>>();

        /// <summary>
        /// Gets or sets the reasons of the world titles by item ID.
        /// </summary>
        public Dictionary<Guid, string> Reasons { get; set; } = new Dictionary<Guid, string>();
    }
}
