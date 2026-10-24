using System;
using System.Globalization;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// Ortszeit der App für Termine. Mit Zeitzone (<c>timeZone=Europe/Berlin</c>) stimmen Uhrzeiten auch über die Umstellung
    /// zwischen Sommer- und Winterzeit hinweg; ein fester Versatz (<c>utcOffsetMinutes</c>) gilt nur für den Augenblick der
    /// Anfrage. Ein Versatz von +60 oder +120 Minuten ohne Zeitzone wird als deutsche Zeit (Europe/Berlin) gelesen.
    /// </summary>
    public sealed class LocalClock
    {
        private readonly TimeZoneInfo? _zone;
        private readonly int _offset;

        private LocalClock(TimeZoneInfo? zone, int offset)
        {
            _zone = zone;
            _offset = offset;
        }

        /// <summary>
        /// Creates the clock from the app's time zone and/or offset.
        /// </summary>
        /// <param name="timeZone">IANA time zone, e.g. „Europe/Berlin“, optional.</param>
        /// <param name="utcOffsetMinutes">Offset of the app right now in minutes, optional.</param>
        /// <param name="serverOffsetMinutes">Offset of the server right now (used without both).</param>
        /// <returns>The clock.</returns>
        public static LocalClock Create(string? timeZone, int? utcOffsetMinutes, int serverOffsetMinutes)
        {
            if (!string.IsNullOrWhiteSpace(timeZone) && timeZone.Length <= 64 && TryZone(timeZone.Trim(), out var zone))
            {
                return new LocalClock(zone, 0);
            }

            var offset = utcOffsetMinutes is >= -840 and <= 840 ? utcOffsetMinutes.Value : serverOffsetMinutes;
            if ((offset == 60 || offset == 120) && TryZone("Europe/Berlin", out var berlin))
            {
                return new LocalClock(berlin, 0);
            }

            return new LocalClock(null, offset);
        }

        /// <summary>
        /// Converts a UTC time to the app's local time.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>Local time.</returns>
        public DateTime ToLocal(DateTime utc)
        {
            var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return _zone != null ? TimeZoneInfo.ConvertTimeFromUtc(value, _zone) : DateTime.SpecifyKind(value.AddMinutes(_offset), DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Converts the start of a local day to UTC.
        /// </summary>
        /// <param name="day">The local day.</param>
        /// <returns>UTC time of local midnight.</returns>
        public DateTime StartOfDayUtc(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return _zone != null ? TimeZoneInfo.ConvertTimeToUtc(local, _zone) : DateTime.SpecifyKind(local.AddMinutes(-_offset), DateTimeKind.Utc);
        }

        /// <summary>
        /// Gets the local day of a UTC time.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>The local day.</returns>
        public DateOnly Day(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

        /// <summary>
        /// Gets the abbreviation of the zone at a time: „MEZ“/„MESZ“ for German time, otherwise „UTC+1“ etc.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>The abbreviation.</returns>
        public string Abbreviation(DateTime utc)
        {
            var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            if (_zone != null && (_zone.Id == "Europe/Berlin" || _zone.Id == "W. Europe Standard Time"))
            {
                return _zone.IsDaylightSavingTime(value) ? "MESZ" : "MEZ";
            }

            var minutes = _zone != null ? (int)_zone.GetUtcOffset(value).TotalMinutes : _offset;
            var sign = minutes < 0 ? "-" : "+";
            minutes = Math.Abs(minutes);
            return "UTC" + sign + (minutes / 60).ToString(CultureInfo.InvariantCulture) + (minutes % 60 == 0 ? string.Empty : ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Gets the local time with zone, e.g. „15:30 Uhr MEZ“.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>The text.</returns>
        public string TimeText(DateTime utc) => ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture) + " Uhr " + Abbreviation(utc);

        private static bool TryZone(string id, out TimeZoneInfo zone)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                zone = TimeZoneInfo.Utc;
                return false;
            }
        }
    }
}
