using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Merkt sich pro Benutzer, wonach er zuletzt gesucht und welche Titel er sich angesehen (Detailseite geöffnet) hat –
    /// als Interesse für die Empfehlungen. Nur auf dem Server; wie lange und wie viele Einträge, stellt der Admin ein
    /// (Standard: 365 Tage und 1000 Einträge je Art). Jeder Benutzer kann es abschalten (dabei wird alles gelöscht) oder
    /// seine Einträge löschen.
    /// </summary>
    public class BehaviorStore
    {
        /// <summary>Default maximum number of entries per kind and user.</summary>
        public const int DefaultMaxEntries = 1000;

        /// <summary>Default number of days an entry is kept.</summary>
        public const int DefaultRetentionDays = 365;

        /// <summary>Kind of a search that found titles by name.</summary>
        public const string SearchKindTitle = "Title";

        /// <summary>Kind of a search that found titles by a person.</summary>
        public const string SearchKindPerson = "Person";

        /// <summary>Kind of a search that found titles by genre.</summary>
        public const string SearchKindGenre = "Genre";

        // Zwischenstände beim Tippen ersetzen einander innerhalb dieser Zeit
        private static readonly TimeSpan TypingWindow = TimeSpan.FromMinutes(2);

        // So lange nach der Suche zählt das Öffnen oder Abspielen eines Treffers als Abschluss
        private static readonly TimeSpan ConfirmWindow = TimeSpan.FromMinutes(30);

        // Nicht abgeschlossene Suchen werden nicht aufbewahrt
        private static readonly TimeSpan UnconfirmedRetention = TimeSpan.FromHours(1);

        private readonly ILogger<BehaviorStore> _logger;
        private readonly string _path;
        private readonly int? _retentionDays;
        private readonly int? _maxEntries;
        private readonly object _lock = new object();
        private Dictionary<string, UserBehavior>? _users;
        private long _version;

        /// <summary>
        /// Initializes a new instance of the <see cref="BehaviorStore"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="path">File of the store.</param>
        /// <param name="retentionDays">Days an entry is kept, optional (otherwise from the configuration).</param>
        /// <param name="maxEntries">Entries per kind and user, optional (otherwise from the configuration).</param>
        public BehaviorStore(ILogger<BehaviorStore> logger, string path, int? retentionDays = null, int? maxEntries = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _retentionDays = retentionDays;
            _maxEntries = maxEntries;
        }

        /// <summary>Gets the number of days an entry is kept.</summary>
        public int RetentionDays => _retentionDays ?? Math.Clamp(Plugin.Instance?.Configuration?.BehaviorRetentionDays ?? DefaultRetentionDays, 7, 1095);

        /// <summary>Gets the maximum number of entries per kind and user.</summary>
        public int MaxEntries => _maxEntries ?? Math.Clamp(Plugin.Instance?.Configuration?.BehaviorMaxEntries ?? DefaultMaxEntries, 50, 5000);

        /// <summary>Gets a number that changes with every change of the stored entries.</summary>
        public long Version => Interlocked.Read(ref _version);

        /// <summary>
        /// Gets a copy of the stored behavior of a user (without entries older than <see cref="RetentionDays"/>).
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
                    Searches = entry.Searches.Where(s => s.At >= limit).Select(s => new SearchEntry { Term = s.Term, At = s.At, Matches = s.Matches.ToList(), Kind = s.Kind, Confirmed = s.Confirmed }).ToList(),
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
        public void AddSearch(Guid userId, string term, IReadOnlyList<Guid> matches, DateTime now) => AddSearch(userId, term, matches, now, SearchKindTitle);

        /// <summary>
        /// Notes a search (unconfirmed until a found title is opened or played). Intermediate states while typing
        /// („Dea“ → „Deadp“ → „Deadpool“) replace each other.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="term">The search term.</param>
        /// <param name="matches">Titles it stands for.</param>
        /// <param name="now">Now (UTC).</param>
        /// <param name="kind">How the titles were found („Title“, „Person“, „Genre“).</param>
        public void AddSearch(Guid userId, string term, IReadOnlyList<Guid> matches, DateTime now, string kind)
        {
            ArgumentNullException.ThrowIfNull(term);
            ArgumentNullException.ThrowIfNull(matches);
            Change(userId, b =>
            {
                b.Searches.RemoveAll(s => !s.Confirmed && now - s.At < TypingWindow && Behavior.SearchTerms.IsTypingStep(s.Term, term));
                var same = b.Searches.LastOrDefault(s => string.Equals(s.Term, term, StringComparison.OrdinalIgnoreCase));
                if (same != null && now - same.At < TimeSpan.FromMinutes(10))
                {
                    same.At = now;
                    same.Matches = matches.ToList();
                    same.Kind = kind;
                }
                else
                {
                    b.Searches.Add(new SearchEntry { Term = term, At = now, Matches = matches.ToList(), Kind = kind });
                }

                Prune(b, now);
            });
        }

        /// <summary>
        /// Confirms the most recent search (within 30 minutes) that found a title the user now opened or played.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="itemIds">The opened or played title (and its series).</param>
        /// <param name="now">Now (UTC).</param>
        /// <returns>True if a search was confirmed.</returns>
        public bool ConfirmSearch(Guid userId, IReadOnlyCollection<Guid> itemIds, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(itemIds);
            var behavior = Get(userId);
            var candidate = behavior.Searches.LastOrDefault(s => !s.Confirmed && now - s.At < ConfirmWindow && s.Matches.Any(itemIds.Contains));
            if (candidate == null)
            {
                return false;
            }

            Change(userId, b =>
            {
                var entry = b.Searches.LastOrDefault(s => !s.Confirmed && now - s.At < ConfirmWindow && s.Matches.Any(itemIds.Contains));
                if (entry != null)
                {
                    entry.Confirmed = true;
                }
            });
            return true;
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

        private void Prune(UserBehavior behavior, DateTime now)
        {
            var max = MaxEntries;
            var limit = now.AddDays(-RetentionDays);
            behavior.Searches.RemoveAll(s => s.At < limit || (!s.Confirmed && now - s.At > UnconfirmedRetention));
            behavior.Views.RemoveAll(v => v.At < limit);
            if (behavior.Searches.Count > max)
            {
                behavior.Searches.RemoveRange(0, behavior.Searches.Count - max);
            }

            if (behavior.Views.Count > max)
            {
                behavior.Views.RemoveRange(0, behavior.Views.Count - max);
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
                Interlocked.Increment(ref _version);
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

            /// <summary>Gets or sets how the titles were found: „Title“, „Person“ or „Genre“ (since 12.53.0).</summary>
            public string Kind { get; set; } = SearchKindTitle;

            /// <summary>Gets or sets a value indicating whether the search was completed – a found title was opened or played (since 12.53.0).
            /// Only completed searches count.</summary>
            public bool Confirmed { get; set; }
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
