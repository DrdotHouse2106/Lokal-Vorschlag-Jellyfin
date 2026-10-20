using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Baut den iCal-Feed (RFC 5545) mit den gemerkten Live-TV-Sendungen – mit Erinnerung vor Beginn.
    /// </summary>
    public static class IcsBuilder
    {
        /// <summary>
        /// Builds the calendar.
        /// </summary>
        /// <param name="entries">Programmes.</param>
        /// <param name="calendarName">Name of the calendar.</param>
        /// <param name="alarmMinutes">Reminder before the start in minutes (0 = none).</param>
        /// <param name="now">Current time (UTC), for DTSTAMP.</param>
        /// <returns>The iCal text (CRLF line endings).</returns>
        public static string Build(IEnumerable<ReminderEntry> entries, string calendarName, int alarmMinutes, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(entries);
            var lines = new List<string>
            {
                "BEGIN:VCALENDAR",
                "VERSION:2.0",
                "PRODID:-//Quallenkinoplugin//Jellyfin Live-TV Erinnerungen//DE",
                "CALSCALE:GREGORIAN",
                "METHOD:PUBLISH",
                "X-WR-CALNAME:" + Escape(calendarName),
                "REFRESH-INTERVAL;VALUE=DURATION:PT1H",
                "X-PUBLISHED-TTL:PT1H"
            };
            var list = entries.OrderBy(e => e.StartDate).ToList();
            if (list.Count == 0)
            {
                // iOS lehnt Abos ohne ein einziges Ereignis ab – daher ein ganztägiger Platzhalter weit in der Vergangenheit
                lines.Add("BEGIN:VEVENT");
                lines.Add("UID:platzhalter@localrecs");
                lines.Add("DTSTAMP:" + Stamp(now));
                lines.Add("DTSTART;VALUE=DATE:20000101");
                lines.Add("DTEND;VALUE=DATE:20000102");
                lines.Add("SUMMARY:" + Escape(calendarName));
                lines.Add("TRANSP:TRANSPARENT");
                lines.Add("END:VEVENT");
            }

            foreach (var entry in list)
            {
                var summary = string.IsNullOrWhiteSpace(entry.ChannelName) ? entry.Name : entry.ChannelName + ": " + entry.Name;
                if (!string.IsNullOrWhiteSpace(entry.EpisodeTitle))
                {
                    summary += " – " + entry.EpisodeTitle;
                }

                lines.Add("BEGIN:VEVENT");
                lines.Add("UID:" + entry.ProgramId + "@localrecs");
                lines.Add("DTSTAMP:" + Stamp(now));
                lines.Add("DTSTART:" + Stamp(entry.StartDate));
                lines.Add("DTEND:" + Stamp(entry.EndDate > entry.StartDate ? entry.EndDate : entry.StartDate.AddMinutes(30)));
                lines.Add("SUMMARY:" + Escape(summary));
                if (!string.IsNullOrWhiteSpace(entry.ChannelName))
                {
                    lines.Add("LOCATION:" + Escape(entry.ChannelName));
                }

                if (!string.IsNullOrWhiteSpace(entry.Overview))
                {
                    lines.Add("DESCRIPTION:" + Escape(entry.Overview));
                }

                lines.Add("CATEGORIES:Live-TV");
                if (alarmMinutes > 0)
                {
                    lines.Add("BEGIN:VALARM");
                    lines.Add("ACTION:DISPLAY");
                    lines.Add("DESCRIPTION:" + Escape(summary));
                    lines.Add("TRIGGER:-PT" + alarmMinutes.ToString(CultureInfo.InvariantCulture) + "M");
                    lines.Add("END:VALARM");
                }

                lines.Add("END:VEVENT");
            }

            lines.Add("END:VCALENDAR");
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                Fold(builder, line);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Escapes a text value (backslash, semicolon, comma, line breaks).
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The escaped value.</returns>
        public static string Escape(string? value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(";", "\\;", StringComparison.Ordinal)
                .Replace(",", "\\,", StringComparison.Ordinal)
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\n", StringComparison.Ordinal);
        }

        private static string Stamp(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }

        // Zeilen nach 75 Bytes (UTF-8) umbrechen, Fortsetzung mit Leerzeichen
        private static void Fold(StringBuilder builder, string line)
        {
            var bytes = 0;
            var first = true;
            foreach (var rune in line.EnumerateRunes())
            {
                var length = rune.Utf8SequenceLength;
                if (bytes + length > (first ? 75 : 74))
                {
                    builder.Append("\r\n ");
                    bytes = 0;
                    first = false;
                }

                builder.Append(rune.ToString());
                bytes += length;
            }

            builder.Append("\r\n");
        }
    }
}
