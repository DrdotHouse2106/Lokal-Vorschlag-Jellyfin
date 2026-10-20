using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Zusätzliche Film-Merkmale aus einer zweiten XMLTV-Datei (der „XMLTV-Quelle für Darsteller“),
    /// wenn die Programmquelle selbst keine Regie, kein Jahr und keine IMDb-Links enthält.
    /// </summary>
    public class EpgExtra
    {
        /// <summary>Gets the directors.</summary>
        public List<string> Directors { get; } = new List<string>();

        /// <summary>Gets or sets the production year.</summary>
        public int? Year { get; set; }

        /// <summary>Gets or sets the IMDb ID.</summary>
        public string? ImdbId { get; set; }
    }
}
