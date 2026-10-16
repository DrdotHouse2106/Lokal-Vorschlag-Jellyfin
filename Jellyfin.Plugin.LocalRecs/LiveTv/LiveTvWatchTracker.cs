using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Ordnet die Zeit, die ein Benutzer einen Sender schaut, den laufenden Sendungen zu.
    /// Jellyfin meldet beim Live-TV nur den Sender – welche Sendung lief, wird hier über das EPG ermittelt.
    /// Senderwechsel (z. B. Fußball-Konferenz auf mehreren Sendern) werden pro Sendung minutengenau gezählt.
    /// </summary>
    public class LiveTvWatchTracker
    {
        /// <summary>
        /// Longest gap between two playback reports that is still counted as watching.
        /// </summary>
        public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

        private readonly LiveTvWatchStore _store;
        private readonly Func<Guid, DateTime, LiveTvWatchEntry?> _resolveProgram;
        private readonly Dictionary<string, SessionState> _sessions = new Dictionary<string, SessionState>();
        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvWatchTracker"/> class.
        /// </summary>
        /// <param name="store">Store for the watched minutes.</param>
        /// <param name="resolveProgram">Returns the program running on a channel at a given time (UTC), or null.
        /// The returned entry must contain BroadcastKey, StartDate and EndDate.</param>
        public LiveTvWatchTracker(LiveTvWatchStore store, Func<Guid, DateTime, LiveTvWatchEntry?> resolveProgram)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _resolveProgram = resolveProgram ?? throw new ArgumentNullException(nameof(resolveProgram));
        }

        /// <summary>
        /// Gets the number of currently tracked playback sessions.
        /// </summary>
        public int ActiveSessions
        {
            get
            {
                lock (_lock)
                {
                    return _sessions.Count;
                }
            }
        }

        /// <summary>
        /// Handles a playback start or progress report.
        /// </summary>
        /// <param name="sessionKey">Key of the playback session (session + user).</param>
        /// <param name="userId">User ID.</param>
        /// <param name="channelId">Channel being watched, or null if the playback is not live TV.</param>
        /// <param name="isPaused">Whether playback is paused.</param>
        /// <param name="now">Time of the report (UTC).</param>
        public void OnProgress(string sessionKey, Guid userId, Guid? channelId, bool isPaused, DateTime now)
        {
            lock (_lock)
            {
                CountElapsed(sessionKey, now);

                if (channelId == null || channelId == Guid.Empty)
                {
                    _sessions.Remove(sessionKey);
                    return;
                }

                _sessions[sessionKey] = new SessionState(userId, channelId.Value, isPaused, now);
            }
        }

        /// <summary>
        /// Handles a playback stop report.
        /// </summary>
        /// <param name="sessionKey">Key of the playback session.</param>
        /// <param name="now">Time of the report (UTC).</param>
        public void OnStopped(string sessionKey, DateTime now)
        {
            lock (_lock)
            {
                CountElapsed(sessionKey, now);
                _sessions.Remove(sessionKey);
            }
        }

        /// <summary>
        /// Forgets sessions that have not reported for longer than <see cref="MaxGap"/> (e.g. a client crashed).
        /// </summary>
        /// <param name="now">Current time (UTC).</param>
        public void RemoveStaleSessions(DateTime now)
        {
            lock (_lock)
            {
                foreach (var key in _sessions.Where(kv => now - kv.Value.LastReport > MaxGap).Select(kv => kv.Key).ToList())
                {
                    _sessions.Remove(key);
                }
            }
        }

        private void CountElapsed(string sessionKey, DateTime now)
        {
            if (!_sessions.TryGetValue(sessionKey, out var state) || state.IsPaused)
            {
                return;
            }

            var elapsed = now - state.LastReport;
            if (elapsed <= TimeSpan.Zero || elapsed > MaxGap)
            {
                // Lücke zu groß (Client hat sich lange nicht gemeldet) – lieber nicht zählen
                return;
            }

            // Zeitraum ggf. auf mehrere Sendungen aufteilen (Sendung endet während des Schauens)
            var from = state.LastReport;
            for (var i = 0; i < 5 && from < now; i++)
            {
                var program = _resolveProgram(state.ChannelId, from);
                if (program == null)
                {
                    return;
                }

                var until = program.EndDate.HasValue && program.EndDate.Value > from && program.EndDate.Value < now
                    ? program.EndDate.Value
                    : now;

                program.UserId = state.UserId;
                _store.AddMinutes(program, (until - from).TotalMinutes, now);
                from = until;
            }
        }

        private sealed class SessionState
        {
            public SessionState(Guid userId, Guid channelId, bool isPaused, DateTime lastReport)
            {
                UserId = userId;
                ChannelId = channelId;
                IsPaused = isPaused;
                LastReport = lastReport;
            }

            public Guid UserId { get; }

            public Guid ChannelId { get; }

            public bool IsPaused { get; }

            public DateTime LastReport { get; }
        }
    }
}
