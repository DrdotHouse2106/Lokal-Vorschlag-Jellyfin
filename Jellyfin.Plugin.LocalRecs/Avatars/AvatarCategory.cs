using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Eine Kategorie des Profilbild-Katalogs (z. B. „Tiere“).
    /// </summary>
    public class AvatarCategory
    {
        /// <summary>Gets or sets the ID (a–z, 0–9, "-" and "_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets the icons.</summary>
        public List<AvatarIcon> Icons { get; } = new List<AvatarIcon>();
    }
}
