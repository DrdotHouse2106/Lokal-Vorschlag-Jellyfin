using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Gemerkte Live-TV-Sendungen und Serien pro Benutzer, dazu der geheime Kalender-Schlüssel (reminders.json).
    /// </summary>
    public class ReminderStore
    {
        // Vergangene Sendungen bleiben noch 2 Tage im Kalender
        private static readonly TimeSpan KeepAfterEnd = TimeSpan.FromDays(2);

        private readonly ILogger<ReminderStore> _logger;
        private readonly string _filePath;
        private readonly object _lock = new object();
        private Dictionary<string, UserReminders> _users = new Dictionary<string, UserReminders>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ReminderStore"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="filePath">Path of the JSON file.</param>
        public ReminderStore(ILogger<ReminderStore> logger, string filePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Load();
        }

        /// <summary>
        /// Gets the remembered single programmes of a user (ended more than 2 days ago are dropped).
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>The programmes.</returns>
        public IReadOnlyList<ReminderEntry> GetPrograms(Guid userId, DateTime now)
        {
            lock (_lock)
            {
                return User(userId, false)?.Programs.Where(p => p.EndDate >= now - KeepAfterEnd).Select(p => p.Clone()).ToList()
                    ?? new List<ReminderEntry>();
            }
        }

        /// <summary>
        /// Gets the remembered series of a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <returns>The series.</returns>
        public IReadOnlyList<SeriesReminder> GetSeries(Guid userId)
        {
            lock (_lock)
            {
                return User(userId, false)?.Series.Select(s => new SeriesReminder { Key = s.Key, Name = s.Name, Added = s.Added }).ToList()
                    ?? new List<SeriesReminder>();
            }
        }

        /// <summary>
        /// Remembers a programme (replaces an existing entry for the same programme).
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="entry">Programme snapshot.</param>
        /// <param name="now">Current time (UTC), for cleaning up.</param>
        public void AddProgram(Guid userId, ReminderEntry entry, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_lock)
            {
                var user = User(userId, true)!;
                user.Programs.RemoveAll(p => p.ProgramId == entry.ProgramId || p.EndDate < now - KeepAfterEnd);
                user.Programs.Add(entry.Clone());
                Save();
            }
        }

        /// <summary>
        /// Forgets a programme.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="programId">Programme ID (N format).</param>
        /// <returns>True if it was remembered.</returns>
        public bool RemoveProgram(Guid userId, string programId)
        {
            lock (_lock)
            {
                var user = User(userId, false);
                var removed = user != null && user.Programs.RemoveAll(p => p.ProgramId == programId) > 0;
                if (removed)
                {
                    Save();
                }

                return removed;
            }
        }

        /// <summary>
        /// Remembers a series (all future broadcasts with the same series key).
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="key">Series key (normalized title).</param>
        /// <param name="name">Display name.</param>
        /// <param name="now">Current time (UTC).</param>
        public void AddSeries(Guid userId, string key, string name, DateTime now)
        {
            lock (_lock)
            {
                var user = User(userId, true)!;
                if (user.Series.All(s => s.Key != key))
                {
                    user.Series.Add(new SeriesReminder { Key = key, Name = name, Added = now });
                    Save();
                }
            }
        }

        /// <summary>
        /// Forgets a series.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="key">Series key.</param>
        /// <returns>True if it was remembered.</returns>
        public bool RemoveSeries(Guid userId, string key)
        {
            lock (_lock)
            {
                var user = User(userId, false);
                var removed = user != null && user.Series.RemoveAll(s => s.Key == key) > 0;
                if (removed)
                {
                    Save();
                }

                return removed;
            }
        }

        /// <summary>
        /// Gets the calendar token of a user, creating one if needed.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="renew">True to create a new token (the old link stops working).</param>
        /// <returns>The token.</returns>
        public string GetToken(Guid userId, bool renew)
        {
            lock (_lock)
            {
                var user = User(userId, true)!;
                if (renew || string.IsNullOrEmpty(user.Token))
                {
                    user.Token = NewToken();
                    Save();
                }

                return user.Token!;
            }
        }

        /// <summary>
        /// Finds the user of a calendar token (constant-time comparison).
        /// </summary>
        /// <param name="token">The token from the link.</param>
        /// <returns>The user ID, or null.</returns>
        public Guid? FindUserByToken(string? token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 32)
            {
                return null;
            }

            var given = System.Text.Encoding.ASCII.GetBytes(token);
            lock (_lock)
            {
                foreach (var pair in _users)
                {
                    if (string.IsNullOrEmpty(pair.Value.Token))
                    {
                        continue;
                    }

                    var stored = System.Text.Encoding.ASCII.GetBytes(pair.Value.Token);
                    if (CryptographicOperations.FixedTimeEquals(given, stored) && Guid.TryParse(pair.Key, out var id))
                    {
                        return id;
                    }
                }
            }

            return null;
        }

        private static string NewToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        }

        private UserReminders? User(Guid userId, bool create)
        {
            var key = userId.ToString("N");
            if (!_users.TryGetValue(key, out var user) && create)
            {
                user = new UserReminders();
                _users[key] = user;
            }

            return user;
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    _users = JsonSerializer.Deserialize<Dictionary<string, UserReminders>>(File.ReadAllText(_filePath)) ?? new Dictionary<string, UserReminders>();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Erinnerungen konnten nicht gelesen werden");
            }
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = _filePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_users));
                File.Move(temp, _filePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Erinnerungen konnten nicht gespeichert werden");
            }
        }

        private sealed class UserReminders
        {
            public string? Token { get; set; }

            public List<ReminderEntry> Programs { get; set; } = new List<ReminderEntry>();

            public List<SeriesReminder> Series { get; set; } = new List<SeriesReminder>();
        }
    }
}
