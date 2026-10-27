using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Ein Eintrag der Reihenfolge nach Handlungszeit („chronologie“ in der Weltenliste): entweder eine Abschnittsüberschrift
    /// (z. B. „Kelvin-Zeitlinie (alternativ)“) oder ein Titel mit fertigem Text (z. B. „2151“) und bei Serien den Staffeln.
    /// Dieselbe Serie darf mehrfach vorkommen (Discovery Staffel 1–2 und 3–5).
    /// </summary>
    public class WorldChronologyEntry
    {
        /// <summary>Gets or sets the section heading (then no title), or null.</summary>
        public string? Section { get; set; }

        /// <summary>Gets or sets the title (TMDB/IMDb/TVDB ID and type), or null for a section.</summary>
        public WorldTitleRef? Title { get; set; }

        /// <summary>Gets or sets the ready text, e.g. „2151“ or „2273–2293“, or null.</summary>
        public string? Label { get; set; }

        /// <summary>Gets or sets the seasons (series only), empty for all.</summary>
        public IReadOnlyList<int> Seasons { get; set; } = Array.Empty<int>();
    }
}
