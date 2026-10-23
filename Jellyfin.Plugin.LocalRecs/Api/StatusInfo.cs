using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalRecs.LiveTv;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Status der Startseiten-Empfehlungen für die Konfigurationsseite.
    /// </summary>
    public class StatusInfo
    {
        /// <summary>
        /// Gets or sets a value indicating whether the Quallenkino interface is registered with File Transformation.
        /// </summary>
        public bool WebIntegrationRegistered { get; set; }

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
        /// Gets or sets the number of playback events received since the server start.
        /// </summary>
        public long PlaybackEventsReceived { get; set; }

        /// <summary>
        /// Gets or sets the number of playback events recognized as live TV since the server start.
        /// </summary>
        public long LiveTvEventsReceived { get; set; }

        /// <summary>
        /// Gets or sets the time a running program was last found in the guide (UTC).
        /// </summary>
        public DateTime? LastProgramResolved { get; set; }

        /// <summary>
        /// Gets or sets the time a watched channel had no program in the guide (UTC).
        /// </summary>
        public DateTime? LastProgramMissing { get; set; }

        /// <summary>
        /// Gets or sets the recently seen playbacks (for troubleshooting live TV tracking).
        /// </summary>
        public IReadOnlyList<LiveTvDiagnostics.PlaybackSeen> RecentPlaybacks { get; set; } = Array.Empty<LiveTvDiagnostics.PlaybackSeen>();

        /// <summary>
        /// Gets or sets a value indicating whether an additional XMLTV source is configured.
        /// </summary>
        public bool XmltvConfigured { get; set; }

        /// <summary>
        /// Gets or sets the number of programmes read from the XMLTV source.
        /// </summary>
        public int XmltvProgrammes { get; set; }

        /// <summary>
        /// Gets or sets the number of programmes with actors or directors in the XMLTV source.
        /// </summary>
        public int XmltvProgrammesWithPeople { get; set; }

        /// <summary>
        /// Gets or sets the time the XMLTV source was last loaded (UTC).
        /// </summary>
        public DateTime? XmltvLastLoaded { get; set; }

        /// <summary>
        /// Gets or sets the error of the last XMLTV load, if any.
        /// </summary>
        public string? XmltvError { get; set; }

        /// <summary>
        /// Gets or sets the plugin version that computed the stored recommendations.
        /// </summary>
        public string? RecommendationsVersion { get; set; }
    }
}
