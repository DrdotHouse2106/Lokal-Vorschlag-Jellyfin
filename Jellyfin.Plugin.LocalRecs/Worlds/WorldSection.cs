namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Ein Abschnitt der normalen Welt-Ansicht (wie die Reihen der Marken-Kacheln bei Disney+), aus „abschnitte“ der Welten-Liste.
    /// </summary>
    public class WorldSection
    {
        /// <summary>Gets or sets the name, e.g. „Realverfilmungen“.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the minimum number of TMDB votes for missing titles to be shown directly (fewer: only under „Alle anzeigen“), or null.</summary>
        public int? MinMissingVotes { get; set; }
    }
}
