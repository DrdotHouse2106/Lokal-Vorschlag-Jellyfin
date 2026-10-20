using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Ergebnis der Regel-Bausteine für eine Sendung: Art, Staffel/Folge und die Begründung (zum Testen).
    /// </summary>
    public class EpgClassification
    {
        /// <summary>Gets or sets a value indicating whether it is a series episode.</summary>
        public bool IsSeries { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a movie.</summary>
        public bool IsMovie { get; set; }

        /// <summary>Gets or sets a value indicating whether it is for kids.</summary>
        public bool IsKids { get; set; }

        /// <summary>Gets or sets a value indicating whether it is news.</summary>
        public bool IsNews { get; set; }

        /// <summary>Gets or sets a value indicating whether it is sports.</summary>
        public bool IsSports { get; set; }

        /// <summary>Gets or sets the season number.</summary>
        public int? Season { get; set; }

        /// <summary>Gets or sets the episode number.</summary>
        public int? Episode { get; set; }

        /// <summary>Gets or sets the episode title.</summary>
        public string? EpisodeTitle { get; set; }

        /// <summary>Gets or sets the production year.</summary>
        public int? Year { get; set; }

        /// <summary>Gets or sets the IMDb ID found in the URLs.</summary>
        public string? ImdbId { get; set; }

        /// <summary>Gets the reasons (which rule decided what).</summary>
        public List<string> Reasons { get; } = new List<string>();
    }
}
