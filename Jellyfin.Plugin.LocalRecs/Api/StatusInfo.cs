using System;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Status der Startseiten-Empfehlungen für die Konfigurationsseite.
    /// </summary>
    public class StatusInfo
    {
        /// <summary>
        /// Gets or sets a value indicating whether the Home Screen Sections plugin is installed.
        /// </summary>
        public bool HomeScreenSectionsInstalled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the sections are registered.
        /// </summary>
        public bool SectionsRegistered { get; set; }

        /// <summary>
        /// Gets or sets the number of users with recommendations.
        /// </summary>
        public int UsersWithRecommendations { get; set; }

        /// <summary>
        /// Gets or sets the time of the last refresh (UTC).
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets the number of live TV broadcasts in the watch history.
        /// </summary>
        public int LiveTvTrackedBroadcasts { get; set; }

        /// <summary>
        /// Gets or sets the number of live TV playbacks currently being watched.
        /// </summary>
        public int LiveTvActiveSessions { get; set; }

        /// <summary>
        /// Gets or sets the plugin version that computed the stored recommendations.
        /// </summary>
        public string? RecommendationsVersion { get; set; }
    }
}
