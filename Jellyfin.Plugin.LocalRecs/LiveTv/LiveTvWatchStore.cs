using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Sammelt die live geschauten Minuten pro Benutzer und Sendung. Die Daten liegen im Arbeitsspeicher
    /// und werden nur gelegentlich (<see cref="SaveIfDirty"/>) als JSON-Datei geschrieben.
    /// </summary>
    public class LiveTvWatchStore
    {
        private readonly ILogger<LiveTvWatchStore> _logger;
        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly Dictionary<string, LiveTvWatchEntry> _entries = new Dictionary<string, LiveTvWatchEntry>();
        private bool _dirty;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvWatchStore"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="filePath">Path of the JSON file used for persistence.</param>
        public LiveTvWatchStore(ILogger<LiveTvWatchStore> logger, string filePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Load();
        }

        /// <summary>
        /// Gets the number of stored broadcasts.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// Adds watched minutes to a broadcast. The metadata is taken from <paramref name="broadcast"/>
        /// the first time the broadcast is seen.
        /// </summary>
        /// <param name="broadcast">Broadcast metadata (UserId and BroadcastKey identify the entry).</param>
        /// <param name="minutes">Minutes to add.</param>
        /// <param name="watchedAt">Time of watching (UTC).</param>
        public void AddMinutes(LiveTvWatchEntry broadcast, double minutes, DateTime watchedAt)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            if (minutes <= 0)
            {
                return;
            }

            var key = broadcast.UserId.ToString("N") + "|" + broadcast.BroadcastKey;
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    entry = Clone(broadcast);
                    entry.Minutes = 0;
                    _entries[key] = entry;
                }

                entry.Minutes += minutes;
                entry.LastWatched = watchedAt;
                _dirty = true;
            }
        }

        /// <summary>
        /// Gets the watched broadcasts of a user that reach the minimum watch time.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="minMinutes">Minimum minutes per broadcast (shorter views count as zapping).</param>
        /// <returns>Copies of the entries.</returns>
        public IReadOnlyList<LiveTvWatchEntry> GetEntries(Guid userId, double minMinutes)
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.UserId == userId && e.Minutes >= minMinutes)
                    .Select(Clone)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets the watched broadcasts of all users that were watched since the given time.
        /// </summary>
        /// <param name="since">Time (UTC).</param>
        /// <param name="minMinutes">Minimum minutes per broadcast.</param>
        /// <returns>Copies of the entries.</returns>
        public IReadOnlyList<LiveTvWatchEntry> GetRecentEntries(DateTime since, double minMinutes)
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.LastWatched >= since && e.Minutes >= minMinutes)
                    .Select(Clone)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes entries that were last watched before the given time.
        /// </summary>
        /// <param name="olderThan">Cut-off time (UTC).</param>
        public void Prune(DateTime olderThan)
        {
            lock (_lock)
            {
                foreach (var key in _entries.Where(kv => kv.Value.LastWatched < olderThan).Select(kv => kv.Key).ToList())
                {
                    _entries.Remove(key);
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// Removes all entries of a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        public void RemoveUser(Guid userId)
        {
            lock (_lock)
            {
                foreach (var key in _entries.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
                {
                    _entries.Remove(key);
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// Writes the data to disk if something changed since the last save.
        /// </summary>
        public void SaveIfDirty()
        {
            List<LiveTvWatchEntry> snapshot;
            lock (_lock)
            {
                if (!_dirty)
                {
                    return;
                }

                snapshot = _entries.Values.Select(Clone).ToList();
                _dirty = false;
            }

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot));
                File.Move(tempPath, _filePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Live-TV-Verlauf konnte nicht gespeichert werden: {Path}", _filePath);
                lock (_lock)
                {
                    _dirty = true;
                }
            }
        }

        private static LiveTvWatchEntry Clone(LiveTvWatchEntry e)
        {
            return new LiveTvWatchEntry
            {
                UserId = e.UserId,
                BroadcastKey = e.BroadcastKey,
                Title = e.Title,
                SeriesName = e.SeriesName,
                EpisodeTitle = e.EpisodeTitle,
                Genres = e.Genres.ToList(),
                People = e.People.ToList(),
                ChannelName = e.ChannelName,
                IsMovie = e.IsMovie,
                IsSports = e.IsSports,
                ProductionYear = e.ProductionYear,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Minutes = e.Minutes,
                LastWatched = e.LastWatched
            };
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                var entries = JsonSerializer.Deserialize<List<LiveTvWatchEntry>>(File.ReadAllText(_filePath));
                foreach (var entry in entries ?? new List<LiveTvWatchEntry>())
                {
                    _entries[entry.UserId.ToString("N") + "|" + entry.BroadcastKey] = entry;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Live-TV-Verlauf konnte nicht gelesen werden: {Path}", _filePath);
            }
        }
    }
}
