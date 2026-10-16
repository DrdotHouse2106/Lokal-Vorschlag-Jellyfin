using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Eine live geschaute Sendung eines Benutzers. Die Metadaten werden beim Schauen kopiert,
    /// weil Jellyfin EPG-Einträge nach dem Ende der Sendung löscht.
    /// </summary>
    public class LiveTvWatchEntry
    {
        /// <summary>
        /// Gets or sets the user ID.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Gets or sets a key identifying the broadcast (channel + start time).
        /// </summary>
        public string BroadcastKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the program title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the series name, if the program belongs to a series.
        /// </summary>
        public string? SeriesName { get; set; }

        /// <summary>
        /// Gets or sets the episode title.
        /// </summary>
        public string? EpisodeTitle { get; set; }

        /// <summary>
        /// Gets or sets the genres.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the people (actors, directors, ...).
        /// </summary>
        public List<string> People { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the channel name.
        /// </summary>
        public string? ChannelName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the program is a movie.
        /// </summary>
        public bool IsMovie { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the program is a sports broadcast.
        /// </summary>
        public bool IsSports { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        public int? ProductionYear { get; set; }

        /// <summary>
        /// Gets or sets the start of the broadcast (UTC).
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Gets or sets the end of the broadcast (UTC).
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Gets or sets the minutes the user watched this broadcast.
        /// </summary>
        public double Minutes { get; set; }

        /// <summary>
        /// Gets or sets the time the user last watched this broadcast (UTC).
        /// </summary>
        public DateTime LastWatched { get; set; }
    }
}
