using System;
using System.Globalization;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Texte für die Karten von „Heute im TV für dich“. Home Screen Sections zeigt pro Karte nur Titel
    /// und eine zweite Zeile – dort steht die Sendezeit und der Sender.
    /// </summary>
    public static class LiveTvDisplay
    {
        private static readonly string[] DayNames = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

        /// <summary>
        /// Resolves the configured time zone (e.g. "Europe/Berlin"), falling back to the server time zone.
        /// </summary>
        /// <param name="timeZoneId">IANA or Windows time zone ID.</param>
        /// <returns>The time zone.</returns>
        public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
                {
                    // Unbekannte Zeitzone: Serverzeit verwenden
                }
            }

            return TimeZoneInfo.Local;
        }

        /// <summary>
        /// Builds the time line of a card, e.g. "Heute 20:15 · ZDF", "Morgen 09:10 · RTL",
        /// "Sa 14:00 · Das Erste" or "Läuft bis 15:00 · ARTE".
        /// </summary>
        /// <param name="startUtc">Start (UTC).</param>
        /// <param name="endUtc">End (UTC).</param>
        /// <param name="channelName">Channel name.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="timeZone">Time zone for display.</param>
        /// <returns>The text.</returns>
        public static string TimeLine(DateTime startUtc, DateTime? endUtc, string? channelName, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(timeZone);

            var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), timeZone);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone);

            string when;
            if (startUtc <= nowUtc && endUtc.HasValue && endUtc.Value > nowUtc)
            {
                var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc), timeZone);
                when = "Läuft bis " + end.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            else if (start.Date == now.Date)
            {
                when = "Heute " + start.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            else if (start.Date == now.Date.AddDays(1))
            {
                when = "Morgen " + start.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            else
            {
                when = DayNames[(int)start.DayOfWeek] + " " + start.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return string.IsNullOrWhiteSpace(channelName) ? when : when + " · " + channelName.Trim();
        }

        /// <summary>
        /// Builds the title of a card. For sports the pairing (episode title) is the important part,
        /// e.g. "Manchester City – Paris Saint-Germain · Fußball: Champions League".
        /// </summary>
        /// <param name="name">Program name.</param>
        /// <param name="episodeTitle">Episode title.</param>
        /// <param name="isSports">Whether the program is sports.</param>
        /// <returns>The title.</returns>
        public static string Title(string? name, string? episodeTitle, bool isSports)
        {
            var title = name?.Trim() ?? string.Empty;
            if (isSports && !string.IsNullOrWhiteSpace(episodeTitle))
            {
                return episodeTitle.Trim() + (title.Length > 0 ? " · " + title : string.Empty);
            }

            return title;
        }
    }
}
