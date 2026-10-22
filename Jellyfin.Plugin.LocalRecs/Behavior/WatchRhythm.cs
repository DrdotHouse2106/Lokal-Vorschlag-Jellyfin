using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Sehrhythmus eines Profils aus der eigenen Wiedergabe-Historie: wie lang das ist, was abends unter der Woche bzw.
    /// am Wochenende geschaut wird, und wann abends üblicherweise Schluss ist („Schlafenszeit“). Daraus: lange Filme
    /// unter der Woche bzw. kurz vor der Schlafenszeit zurückstellen. Ohne genug Historie gelten Startwerte
    /// (Freitag/Samstag abends lang, sonst abends eher kurz, Schluss um 23:30).
    /// </summary>
    public class WatchRhythm
    {
        /// <summary>Content longer than this counts as long (minutes).</summary>
        public const int LongMinutes = 100;

        /// <summary>Plays needed in a slot before it is learned.</summary>
        public const int MinSlotPlays = 10;

        /// <summary>Evenings with activity needed before the bedtime is learned.</summary>
        public const int MinEvenings = 5;

        /// <summary>Default bedtime (hours after midnight of the day, 23.5 = 23:30).</summary>
        public const double DefaultBedtime = 23.5;

        private readonly List<(DateTime Utc, double Minutes)> _plays = new List<(DateTime Utc, double Minutes)>();

        /// <summary>Gets the number of plays.</summary>
        public int Count => _plays.Count;

        /// <summary>
        /// Gets whether a local time is a weekend evening (Friday or Saturday, 17:00–04:00).
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>True for a weekend evening.</returns>
        public static bool IsWeekendEvening(DateTime local)
        {
            var day = local.Hour < 4 ? local.AddDays(-1) : local;
            return IsEvening(local) && day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
        }

        /// <summary>
        /// Gets whether a local time is in the evening or night (17:00–04:00).
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>True for evening or night.</returns>
        public static bool IsEvening(DateTime local) => local.Hour >= 17 || local.Hour < 4;

        /// <summary>
        /// Gets the hour of an evening activity counted from midnight of the evening's day (01:30 → 25.5).
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>Hours.</returns>
        public static double EveningHour(DateTime local) => local.Hour + (local.Minute / 60.0) + (local.Hour < 4 ? 24 : 0);

        /// <summary>
        /// Gets the factor for a title by its runtime: before the bedtime only what still fits, on weekday evenings long movies count less.
        /// </summary>
        /// <param name="runtimeMinutes">Runtime in minutes (episodes: one episode), or null.</param>
        /// <param name="remainingMinutes">Minutes until the usual bedtime, or null outside the evening.</param>
        /// <param name="preferShort">True if short content fits better now (learned or default).</param>
        /// <returns>Factor between 0.35 and 1.</returns>
        public static double RuntimeFactor(double? runtimeMinutes, double? remainingMinutes, bool preferShort)
        {
            if (runtimeMinutes is not double runtime || runtime <= 0)
            {
                return 1;
            }

            var factor = 1.0;
            if (remainingMinutes is double remaining && runtime > remaining + 15)
            {
                factor = runtime > remaining + 45 ? 0.35 : 0.6;
            }

            if (preferShort && runtime > LongMinutes)
            {
                factor = Math.Min(factor, 0.6);
            }

            return factor;
        }

        /// <summary>
        /// Adds a play.
        /// </summary>
        /// <param name="utc">End of the play (UTC).</param>
        /// <param name="runtimeMinutes">Runtime of the title in minutes.</param>
        public void Add(DateTime utc, double? runtimeMinutes)
        {
            _plays.Add((utc, runtimeMinutes ?? 0));
        }

        /// <summary>
        /// Gets the share of long content among the plays in a slot (null if fewer than <see cref="MinSlotPlays"/>).
        /// </summary>
        /// <param name="weekend">True for weekend evenings, false for weekday evenings.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>Share between 0 and 1, or null.</returns>
        public double? LongShare(bool weekend, int utcOffsetMinutes)
        {
            var slot = _plays
                .Where(p => p.Minutes > 0)
                .Select(p => (Local: p.Utc.AddMinutes(utcOffsetMinutes), p.Minutes))
                .Where(p => IsEvening(p.Local) && IsWeekendEvening(p.Local) == weekend)
                .ToList();
            return slot.Count < MinSlotPlays ? null : (double)slot.Count(p => p.Minutes > LongMinutes) / slot.Count;
        }

        /// <summary>
        /// Gets whether short content fits better now: on weekday evenings when the profile watches less long content there than
        /// on weekend evenings (learned), without enough history on weekday evenings (default).
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>True if short content fits better.</returns>
        public bool PreferShort(DateTime local, int utcOffsetMinutes)
        {
            if (!IsEvening(local) || IsWeekendEvening(local))
            {
                return false;
            }

            var weekday = LongShare(false, utcOffsetMinutes);
            var weekend = LongShare(true, utcOffsetMinutes);
            if (weekday is double w && weekend is double e)
            {
                return w + 0.1 < e;
            }

            return weekday is not double learned || learned < 0.5;
        }

        /// <summary>
        /// Gets the usual bedtime: the 80th percentile of the last activity of the evenings (hours from midnight of the evening's
        /// day, e.g. 23.5 or 24.75); <see cref="DefaultBedtime"/> with fewer than <see cref="MinEvenings"/> evenings.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>The bedtime in hours.</returns>
        public double Bedtime(int utcOffsetMinutes)
        {
            var lastPerEvening = _plays
                .Select(p => p.Utc.AddMinutes(utcOffsetMinutes))
                .Where(IsEvening)
                .GroupBy(l => DateOnly.FromDateTime(l.Hour < 4 ? l.AddDays(-1) : l))
                .Select(g => g.Max(EveningHour))
                .OrderBy(h => h)
                .ToList();
            if (lastPerEvening.Count < MinEvenings)
            {
                return DefaultBedtime;
            }

            return lastPerEvening[Math.Min(lastPerEvening.Count - 1, (int)Math.Ceiling(lastPerEvening.Count * 0.8) - 1)];
        }

        /// <summary>
        /// Gets the minutes until the usual bedtime from 19:00 on, otherwise null.
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>Minutes (at least 0), or null.</returns>
        public double? RemainingMinutes(DateTime local, int utcOffsetMinutes)
        {
            if (!(local.Hour >= 19 || local.Hour < 4))
            {
                return null;
            }

            return Math.Max(0, (Bedtime(utcOffsetMinutes) - EveningHour(local)) * 60);
        }
    }
}
