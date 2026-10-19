using System;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Eine gemerkte Serie: alle künftigen Ausstrahlungen mit gleichem (normalisiertem) Titel.
    /// </summary>
    public class SeriesReminder
    {
        /// <summary>Gets or sets the series key (normalized title).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets when the series was remembered (UTC).</summary>
        public DateTime Added { get; set; }
    }
}
