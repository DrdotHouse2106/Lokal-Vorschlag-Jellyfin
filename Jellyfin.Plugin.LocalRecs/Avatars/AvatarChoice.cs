using System;

namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Gewähltes Profilbild eines Benutzers (Icon und Farbe), damit die Auswahl beim nächsten Öffnen vorbelegt ist.
    /// </summary>
    public class AvatarChoice
    {
        /// <summary>Gets or sets the icon key "category/icon".</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>Gets or sets the color (#RRGGBB).</summary>
        public string Color { get; set; } = string.Empty;

        /// <summary>Gets or sets the time of the choice (UTC).</summary>
        public DateTime Updated { get; set; }
    }
}
