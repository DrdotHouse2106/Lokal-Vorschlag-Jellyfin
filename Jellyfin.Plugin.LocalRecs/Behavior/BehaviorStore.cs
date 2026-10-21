using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Merkt sich pro Benutzer, wonach er zuletzt gesucht und welche Titel er sich angesehen (Detailseite geöffnet) hat –
    /// als kurzfristiges Interesse für die Empfehlungen. Nur auf dem Server, höchstens 30 Tage und 100 Einträge je Art;
    /// jeder Benutzer kann es abschalten (dabei wird alles gelöscht) oder seine Einträge löschen.
    /// </summary>
    public class BehaviorStore
    {
        /// <summary>Maximum number of entries per kind and user.</summary>
        public const int MaxEntries = 100;

        /// <summary>Days an entry is kept.</summary>
        public const int RetentionDays = 30;

        private readonly ILogger<BehaviorStore> _logger;
        private readonly string _path;
        private readonly object _lock = new object();
        private Dictionary<string, UserBehavior>? _users;

        /// <summary>
        /// Initializes a new instance of the <see cref="BehaviorStore"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="path">File of the store.</param>
        public BehaviorStore(ILogger<BehaviorStore> logger, string path)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>
        /// Gets a copy of the stored behavior of a user (without entries older than 30 days).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>The behavior.</returns>
        public UserBehavior Get(Guid userId)
        {
            lock (_lock)
            {
                var entry = Find(userId);
                if (entry == null)
                {
                    return new UserBehavior();
                }

                var limit = DateTime.UtcNow.AddDays(-RetentionDays);
                return new UserBehavior
                {
                    Enabled = entry.Enabled,
                    Searches = entry.Searches.Where(s => s.At >= limit).Select(s => new SearchEntry { Term = s.Term, At = s.At, Matches = s.Matches.ToList() }).ToList(),
                    Views = entry.Views.Where(v => v.At >= limit).Select(v => new ViewEntry { ItemId = v.ItemId, At = v.At }).ToList()
                };
            }
        }

        /// <summary>
        /// Gets a value indicating whether a user lets the plugin note searches and viewed titles (default on).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>True if on.</returns>
        public bool IsEnabled(Guid userId)
        {
            lock (_lock)
            {
                return Find(userId)?.Enabled != false;
            }
        }

        /// <summary>
        /// Switches noting on or off for a user; switching off deletes the stored entries.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="enabled">On or off.</param>
        public void SetEnabled(Guid userId, bool enabled)
        {
            Change(userId, b =>
            {
                b.Enabled = enabled;
                if (!enabled)
                {
                    b.Searches.Clear();
                    b.Views.Clear();
                }
            });
        }

        /// <summary>
        /// Deletes all entries of a user (the setting stays).
        /// </summary>
        /// <param name="userId">The user.</param>
        public void Clear(Guid userId)
        {
            Change(userId, b =>
            {
                b.Searches.Clear();
                b.Views.Clear();
            });
        }

        /// <summary>
        /// Notes a search (the same term within 10 minutes only updates the time).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="term">Search term.</param>
        /// <param name="matches">Library titles the term stands for.</param>
        /// <param name="now">Current time (UTC).</param>
        public void AddSearch(Guid userId, string term, IReadOnlyList<Guid> matches, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(term);
            ArgumentNullException.ThrowIfNull(matches);
            Change(userId, b =>
            {
                var same = b.Searches.LastOrDefault(s => string.Equals(s.Term, term, StringComparison.OrdinalIgnoreCase));
                if (same != null && now - same.At < TimeSpan.FromMinutes(10))
                {
                    same.At = now;
                    same.Matches = matches.ToList();
                }
                else
                {
                    b.Searches.Add(new SearchEntry { Term = term, At = now, Matches = matches.ToList() });
                }

                Prune(b, now);
            });
        }

        /// <summary>
        /// Notes that a user opened the detail page of a title (the same title within an hour only once).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="itemId">The title.</param>
        /// <param name="now">Current time (UTC).</param>
        public void AddView(Guid userId, Guid itemId, DateTime now)
        {
            Change(userId, b =>
            {
                var same = b.Views.LastOrDefault(v => v.ItemId == itemId);
                if (same != null && now - same.At < TimeSpan.FromHours(1))
                {
                    return;
                }

                b.Views.Add(new ViewEntry { ItemId = itemId, At = now });
                Prune(b, now);
            });
        }

        private static void Prune(UserBehavior behavior, DateTime now)
        {
            var limit = now.AddDays(-RetentionDays);
            behavior.Searches.RemoveAll(s => s.At < limit);
            behavior.Views.RemoveAll(v => v.At < limit);
            if (behavior.Searches.Count > MaxEntries)
            {
                behavior.Searches.RemoveRange(0, behavior.Searches.Count - MaxEntries);
            }

            if (behavior.Views.Count > MaxEntries)
            {
                behavior.Views.RemoveRange(0, behavior.Views.Count - MaxEntries);
            }
        }

        private UserBehavior? Find(Guid userId)
        {
            Load();
            return _users!.TryGetValue(userId.ToString("N"), out var entry) ? entry : null;
        }

        private void Change(Guid userId, Action<UserBehavior> change)
        {
            string json;
            lock (_lock)
            {
                Load();
                var key = userId.ToString("N");
                if (!_users!.TryGetValue(key, out var entry))
                {
                    _users[key] = entry = new UserBehavior();
                }

                change(entry);
                json = JsonSerializer.Serialize(_users);
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Verhaltensdaten konnten nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private void Load()
        {
            if (_users != null)
            {
                return;
            }

            _users = new Dictionary<string, UserBehavior>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, UserBehavior>>(File.ReadAllText(_path));
                    if (loaded != null)
                    {
                        _users = new Dictionary<string, UserBehavior>(loaded, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Verhaltensdaten konnten nicht gelesen werden: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Stored behavior of a user.
        /// </summary>
        public class UserBehavior
        {
            /// <summary>Gets or sets a value indicating whether noting is on (null = default on).</summary>
            public bool? Enabled { get; set; }

            /// <summary>Gets or sets the searches, oldest first.</summary>
            public List<SearchEntry> Searches { get; set; } = new List<SearchEntry>();

            /// <summary>Gets or sets the viewed titles, oldest first.</summary>
            public List<ViewEntry> Views { get; set; } = new List<ViewEntry>();
        }

        /// <summary>
        /// A search.
        /// </summary>
        public class SearchEntry
        {
            /// <summary>Gets or sets the search term.</summary>
            public string Term { get; set; } = string.Empty;

            /// <summary>Gets or sets when it was searched (UTC).</summary>
            public DateTime At { get; set; }

            /// <summary>Gets or sets the library titles the term stands for.</summary>
            public List<Guid> Matches { get; set; } = new List<Guid>();
        }

        /// <summary>
        /// A viewed title (detail page opened).
        /// </summary>
        public class ViewEntry
        {
            /// <summary>Gets or sets the title.</summary>
            public Guid ItemId { get; set; }

            /// <summary>Gets or sets when it was viewed (UTC).</summary>
            public DateTime At { get; set; }
        }
    }
}
