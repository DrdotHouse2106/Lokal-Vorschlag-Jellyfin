using System;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Anfrage von „Home Screen Sections“ an einen Startseiten-Bereich.
    /// </summary>
    public class SectionRequest
    {
        /// <summary>
        /// Gets or sets the ID of the user whose home screen is being built.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Gets or sets the additional data given at registration (section type).
        /// </summary>
        public string? AdditionalData { get; set; }
    }
}
