using System;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Eine gemerkte Live-TV-Sendung (Momentaufnahme – Sendungen verschwinden nach der Ausstrahlung aus dem Programmführer).
    /// </summary>
    public class ReminderEntry
    {
        /// <summary>Gets or sets the programme ID (N format).</summary>
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>Gets or sets the channel ID (N format).</summary>
        public string? ChannelId { get; set; }

        /// <summary>Gets or sets the channel name.</summary>
        public string? ChannelName { get; set; }

        /// <summary>Gets or sets the series key (normalized title) the programme belongs to.</summary>
        public string? SeriesKey { get; set; }

        /// <summary>Gets or sets the title.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the episode title.</summary>
        public string? EpisodeTitle { get; set; }

        /// <summary>Gets or sets the description.</summary>
        public string? Overview { get; set; }

        /// <summary>Gets or sets the start (UTC).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Gets or sets the end (UTC).</summary>
        public DateTime EndDate { get; set; }

        /// <summary>Gets or sets a value indicating whether the entry comes from a remembered series.</summary>
        public bool IsSeries { get; set; }

        /// <summary>
        /// Creates a copy.
        /// </summary>
        /// <returns>The copy.</returns>
        public ReminderEntry Clone() => (ReminderEntry)MemberwiseClone();
    }
}
