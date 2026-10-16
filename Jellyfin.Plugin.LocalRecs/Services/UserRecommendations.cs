using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Die Empfehlungen eines Benutzers (Item-IDs aus der echten Bibliothek, nach Relevanz sortiert).
    /// </summary>
    public class UserRecommendations
    {
        /// <summary>
        /// Gets or sets the recommended movie IDs.
        /// </summary>
        public IReadOnlyList<Guid> Movies { get; set; } = Array.Empty<Guid>();

        /// <summary>
        /// Gets or sets the recommended series IDs.
        /// </summary>
        public IReadOnlyList<Guid> Series { get; set; } = Array.Empty<Guid>();
    }
}
