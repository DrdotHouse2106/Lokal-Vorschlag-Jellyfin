using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Fehlersuche für die Live-TV-Erfassung: merkt sich die zuletzt gesehenen Wiedergaben (aus Ereignissen
    /// und der minütlichen Abfrage), damit man auf der Einstellungsseite sieht, ob und wie Live-TV gemeldet wird.
    /// Liegt nur im Arbeitsspeicher.
    /// </summary>
    public static class LiveTvDiagnostics
    {
        private const int MaxEntries = 10;

        private static readonly object Lock = new object();
        private static readonly LinkedList<PlaybackSeen> Recent = new LinkedList<PlaybackSeen>();
        private static long _eventCount;
        private static long _liveTvEventCount;
        private static DateTime? _lastProgramResolved;
        private static DateTime? _lastProgramMissing;

        /// <summary>
        /// Gets the number of playback events received since the server start.
        /// </summary>
        public static long EventCount => System.Threading.Interlocked.Read(ref _eventCount);

        /// <summary>
        /// Gets the number of playback events that were recognized as live TV.
        /// </summary>
        public static long LiveTvEventCount => System.Threading.Interlocked.Read(ref _liveTvEventCount);

        /// <summary>
        /// Gets the time a running program was last found in the guide (UTC).
        /// </summary>
        public static DateTime? LastProgramResolved
        {
            get
            {
                lock (Lock)
                {
                    return _lastProgramResolved;
                }
            }
        }

        /// <summary>
        /// Gets the time a live TV channel was watched but no program was found in the guide (UTC).
        /// </summary>
        public static DateTime? LastProgramMissing
        {
            get
            {
                lock (Lock)
                {
                    return _lastProgramMissing;
                }
            }
        }

        /// <summary>
        /// Records a playback seen in an event or in the session poll.
        /// </summary>
        /// <param name="seen">The playback.</param>
        /// <param name="fromEvent">Whether it came from a playback event (otherwise from the session poll).</param>
        /// <returns>True if this is the first time live TV was seen since the server start.</returns>
        public static bool Record(PlaybackSeen seen, bool fromEvent)
        {
            ArgumentNullException.ThrowIfNull(seen);
            var firstLiveTv = false;
            if (fromEvent)
            {
                System.Threading.Interlocked.Increment(ref _eventCount);
                if (seen.IsLiveTv && System.Threading.Interlocked.Increment(ref _liveTvEventCount) == 1)
                {
                    firstLiveTv = true;
                }
            }

            lock (Lock)
            {
                // Gleiche Wiedergabe nur aktualisieren statt jede Minute neu einzutragen
                var existing = Recent.FirstOrDefault(r => r.Client == seen.Client && r.ItemName == seen.ItemName && r.UserName == seen.UserName && r.Source == seen.Source);
                if (existing != null)
                {
                    Recent.Remove(existing);
                }

                Recent.AddFirst(seen);
                while (Recent.Count > MaxEntries)
                {
                    Recent.RemoveLast();
                }
            }

            return firstLiveTv;
        }

        /// <summary>
        /// Records whether a program could be found in the guide for a watched channel.
        /// </summary>
        /// <param name="found">Whether a program was found.</param>
        /// <param name="now">Current time (UTC).</param>
        public static void RecordProgramLookup(bool found, DateTime now)
        {
            lock (Lock)
            {
                if (found)
                {
                    _lastProgramResolved = now;
                }
                else
                {
                    _lastProgramMissing = now;
                }
            }
        }

        /// <summary>
        /// Gets the recently seen playbacks, newest first.
        /// </summary>
        /// <returns>Copy of the list.</returns>
        public static IReadOnlyList<PlaybackSeen> GetRecent()
        {
            lock (Lock)
            {
                return Recent.ToList();
            }
        }

        /// <summary>
        /// A playback seen by the plugin.
        /// </summary>
        public class PlaybackSeen
        {
            /// <summary>Gets or sets the time (UTC).</summary>
            public DateTime Time { get; set; }

            /// <summary>Gets or sets the source ("Ereignis" or "Abfrage").</summary>
            public string Source { get; set; } = string.Empty;

            /// <summary>Gets or sets the client name and device.</summary>
            public string? Client { get; set; }

            /// <summary>Gets or sets the user name.</summary>
            public string? UserName { get; set; }

            /// <summary>Gets or sets the item type as reported by Jellyfin.</summary>
            public string? ItemType { get; set; }

            /// <summary>Gets or sets the item name.</summary>
            public string? ItemName { get; set; }

            /// <summary>Gets or sets a value indicating whether the item was recognized as a live TV channel.</summary>
            public bool IsLiveTv { get; set; }
        }
    }
}
