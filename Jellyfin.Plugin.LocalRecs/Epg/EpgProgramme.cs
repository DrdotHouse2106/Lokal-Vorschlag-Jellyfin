using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Eine Sendung aus einer XMLTV-Datei, so wie sie dort steht (noch ohne Einordnung als Film oder Serie).
    /// </summary>
    public class EpgProgramme
    {
        /// <summary>Gets or sets the XMLTV channel ID.</summary>
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>Gets or sets the start (with the offset from the file).</summary>
        public DateTimeOffset Start { get; set; }

        /// <summary>Gets or sets the end.</summary>
        public DateTimeOffset Stop { get; set; }

        /// <summary>Gets or sets the title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the sub-title (usually the episode title).</summary>
        public string? SubTitle { get; set; }

        /// <summary>Gets or sets the description.</summary>
        public string? Description { get; set; }

        /// <summary>Gets the categories (genres) in file order.</summary>
        public List<string> Categories { get; } = new List<string>();

        /// <summary>Gets the directors.</summary>
        public List<string> Directors { get; } = new List<string>();

        /// <summary>Gets or sets the value of &lt;date&gt; (production year or date).</summary>
        public string? Date { get; set; }

        /// <summary>Gets the URLs (e.g. IMDb).</summary>
        public List<string> Urls { get; } = new List<string>();

        /// <summary>Gets or sets the episode number in the xmltv_ns system.</summary>
        public string? EpisodeNumXmltvNs { get; set; }

        /// <summary>Gets or sets the episode number as shown on screen (e.g. S02E02).</summary>
        public string? EpisodeNumOnscreen { get; set; }

        /// <summary>Gets or sets the programme ID from the file (dd_progid), if any.</summary>
        public string? ProgramId { get; set; }

        /// <summary>Gets or sets the image (icon) URL.</summary>
        public string? IconUrl { get; set; }

        /// <summary>Gets or sets the still image URL.</summary>
        public string? StillUrl { get; set; }

        /// <summary>Gets or sets the backdrop image URL.</summary>
        public string? BackdropUrl { get; set; }

        /// <summary>Gets or sets the age rating.</summary>
        public string? Rating { get; set; }

        /// <summary>Gets or sets the star rating (0–10).</summary>
        public float? StarRating { get; set; }

        /// <summary>Gets or sets a value indicating whether the programme was shown before.</summary>
        public bool PreviouslyShown { get; set; }

        /// <summary>Gets or sets a value indicating whether the programme is new.</summary>
        public bool IsNew { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a premiere.</summary>
        public bool Premiere { get; set; }

        /// <summary>Gets or sets a value indicating whether it is live.</summary>
        public bool Live { get; set; }

        /// <summary>Gets the duration in minutes.</summary>
        public double Minutes => (Stop - Start).TotalMinutes;
    }
}
