using System;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>A monitored movie from Radarr.</summary>
    public class ArrMovie
    {
        /// <summary>Gets or sets Radarr's movie ID.</summary>
        public int RadarrId { get; set; }

        /// <summary>Gets or sets the title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the year.</summary>
        public int? Year { get; set; }

        /// <summary>Gets or sets the overview.</summary>
        public string? Overview { get; set; }

        /// <summary>Gets or sets the TMDB ID.</summary>
        public string? TmdbId { get; set; }

        /// <summary>Gets or sets the IMDb ID.</summary>
        public string? ImdbId { get; set; }

        /// <summary>Gets or sets the certification (e.g. „12“ or „PG-13“).</summary>
        public string? Certification { get; set; }

        /// <summary>Gets or sets Radarr's root folder (or the movie's folder).</summary>
        public string? RootFolderPath { get; set; }

        /// <summary>Gets or sets the cinema release (UTC).</summary>
        public DateTime? InCinemas { get; set; }

        /// <summary>Gets or sets the digital release (UTC).</summary>
        public DateTime? DigitalRelease { get; set; }

        /// <summary>Gets or sets the disc release (UTC).</summary>
        public DateTime? PhysicalRelease { get; set; }

        /// <summary>Gets or sets a value indicating whether Radarr has a poster.</summary>
        public bool HasPoster { get; set; }

        /// <summary>Gets or sets a value indicating whether Radarr has a fanart.</summary>
        public bool HasFanart { get; set; }
    }
}
