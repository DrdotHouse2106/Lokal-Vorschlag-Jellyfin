namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Sender aus einer XMLTV-Datei.
    /// </summary>
    public class EpgChannel
    {
        /// <summary>Gets or sets the XMLTV channel ID.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the channel number (lcn or numeric display name), if any.</summary>
        public string? Number { get; set; }

        /// <summary>Gets or sets the logo URL.</summary>
        public string? IconUrl { get; set; }
    }
}
