using System;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>An upcoming episode from Sonarr.</summary>
    public class ArrEpisode
    {
        /// <summary>Gets or sets Sonarr's series ID.</summary>
        public int SonarrSeriesId { get; set; }

        /// <summary>Gets or sets the series title.</summary>
        public string SeriesTitle { get; set; } = string.Empty;

        /// <summary>Gets or sets the TVDB ID of the series.</summary>
        public string? TvdbId { get; set; }

        /// <summary>Gets or sets the TMDB ID of the series.</summary>
        public string? TmdbId { get; set; }

        /// <summary>Gets or sets the IMDb ID of the series.</summary>
        public string? ImdbId { get; set; }

        /// <summary>Gets or sets the season number.</summary>
        public int SeasonNumber { get; set; }

        /// <summary>Gets or sets the episode number.</summary>
        public int EpisodeNumber { get; set; }

        /// <summary>Gets or sets the episode title.</summary>
        public string? Title { get; set; }

        /// <summary>Gets or sets the episode overview.</summary>
        public string? Overview { get; set; }

        /// <summary>Gets or sets the network of the series (e.g. „Paramount+“).</summary>
        public string? Network { get; set; }

        /// <summary>Gets or sets the air time (UTC).</summary>
        public DateTime AirDateUtc { get; set; }
    }
}
