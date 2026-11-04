using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Speichert „Weniger davon“ für „Heute im TV für dich“ je Benutzer (Datei <c>livetv-weniger.json</c> im
    /// Plugin-Datenverzeichnis). Geschrieben wird im Lock, damit kein älterer Stand einen neueren überschreibt.
    /// </summary>
    public class LiveTvFeedbackStore
    {
        /// <summary>Maximum length of a title or genre (longer ones are shortened).</summary>
        public const int MaxTextLength = 80;

        /// <summary>Maximum number of show marks per user (the oldest are dropped).</summary>
        public const int MaxTitles = 300;

        /// <summary>Maximum number of genre marks per user (the oldest are dropped).</summary>
        public const int MaxGenres = 50;

        private readonly ILogger<LiveTvFeedbackStore> _logger;
        private readonly string _path;
        private readonly object _lock = new object();
        private Dictionary<string, UserData>? _users;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvFeedbackStore"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="path">Path of the JSON file.</param>
        public LiveTvFeedbackStore(ILogger<LiveTvFeedbackStore> logger, string path)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>
        /// Shortens a title or genre to <see cref="MaxTextLength"/> characters.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The shortened text.</returns>
        public static string Shorten(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            var trimmed = text.Trim();
            return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
        }

        /// <summary>
        /// Gets the marks of a user for the ranking.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>The marks.</returns>
        public LiveTvDislikes GetDislikes(Guid userId)
        {
            lock (_lock)
            {
                var data = Find(userId);
                if (data == null)
                {
                    return LiveTvDislikes.Empty;
                }

                return new LiveTvDislikes(
                    data.Titles.ToDictionary(t => t.Key, t => t.Value.Kind, StringComparer.Ordinal),
                    data.Genres.Keys.ToHashSet(StringComparer.Ordinal));
            }
        }

        /// <summary>
        /// Gets all marks of a user, newest first (for display and „rückgängig“).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>The marks.</returns>
        public IReadOnlyList<Mark> GetMarks(Guid userId)
        {
            lock (_lock)
            {
                var data = Find(userId);
                if (data == null)
                {
                    return Array.Empty<Mark>();
                }

                return data.Titles.Select(t => new Mark { Kind = t.Value.Kind, Key = t.Key, Title = t.Value.Name, At = t.Value.At })
                    .Concat(data.Genres.Select(g => new Mark { Kind = LiveTvDislikes.GenreLess, Key = g.Key, Genre = g.Value.Name, At = g.Value.At }))
                    .OrderByDescending(m => m.At)
                    .ToList();
            }
        }

        /// <summary>
        /// Marks a show („weniger“ or „nie“); a later mark replaces the earlier one.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="titleKey">Normalized title.</param>
        /// <param name="title">Display title.</param>
        /// <param name="kind">„weniger“ or „nie“.</param>
        /// <param name="now">Time (UTC).</param>
        public void MarkTitle(Guid userId, string titleKey, string title, string kind, DateTime now)
        {
            Change(userId, data =>
            {
                data.Titles[Shorten(titleKey)] = new Entry { Kind = kind, Name = Shorten(title), At = now };
                Trim(data.Titles, MaxTitles);
            });
        }

        /// <summary>
        /// Marks a genre („genre_weniger“).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="genreKey">Normalized genre.</param>
        /// <param name="genre">Display name.</param>
        /// <param name="now">Time (UTC).</param>
        public void MarkGenre(Guid userId, string genreKey, string genre, DateTime now)
        {
            Change(userId, data =>
            {
                data.Genres[Shorten(genreKey)] = new Entry { Kind = LiveTvDislikes.GenreLess, Name = Shorten(genre), At = now };
                Trim(data.Genres, MaxGenres);
            });
        }

        /// <summary>
        /// Removes the mark of a show.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="titleKey">Normalized title.</param>
        /// <returns>True if something was removed.</returns>
        public bool RemoveTitle(Guid userId, string titleKey)
        {
            var removed = false;
            Change(userId, data => removed = data.Titles.Remove(Shorten(titleKey)));
            return removed;
        }

        /// <summary>
        /// Removes the mark of a genre.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="genreKey">Normalized genre.</param>
        /// <returns>True if something was removed.</returns>
        public bool RemoveGenre(Guid userId, string genreKey)
        {
            var removed = false;
            Change(userId, data => removed = data.Genres.Remove(Shorten(genreKey)));
            return removed;
        }

        // Älteste Einträge verwerfen, wenn es zu viele sind (Datei und Speicher bleiben klein)
        private static void Trim(Dictionary<string, Entry> entries, int max)
        {
            foreach (var old in entries.OrderBy(e => e.Value.At).Take(entries.Count - max).Select(e => e.Key).ToList())
            {
                entries.Remove(old);
            }
        }

        private UserData? Find(Guid userId)
        {
            Load();
            return _users!.TryGetValue(userId.ToString("N"), out var data) ? data : null;
        }

        private void Change(Guid userId, Action<UserData> change)
        {
            lock (_lock)
            {
                Load();
                var key = userId.ToString("N");
                if (!_users!.TryGetValue(key, out var data))
                {
                    _users[key] = data = new UserData();
                }

                change(data);
                if (data.Titles.Count == 0 && data.Genres.Count == 0)
                {
                    _users.Remove(key);
                }

                // Im Lock schreiben, damit kein älterer Stand einen neueren überschreibt
                Save(JsonSerializer.Serialize(_users));
            }
        }

        private void Save(string json)
        {
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
                _logger.LogWarning("Live-TV „Weniger davon“ konnte nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private void Load()
        {
            if (_users != null)
            {
                return;
            }

            _users = new Dictionary<string, UserData>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, UserData>>(File.ReadAllText(_path));
                    if (loaded != null)
                    {
                        foreach (var (key, value) in loaded)
                        {
                            _users[key] = value;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                _logger.LogWarning("Live-TV „Weniger davon“ konnte nicht gelesen werden: {Message}", ex.Message);
            }
        }

        /// <summary>A mark as returned by the API.</summary>
        public class Mark
        {
            /// <summary>Gets or sets the kind: „weniger“, „nie“ or „genre_weniger“.</summary>
            public string Kind { get; set; } = string.Empty;

            /// <summary>Gets or sets the normalized title or genre (for „rückgängig“).</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the display title of the show (title marks).</summary>
            public string? Title { get; set; }

            /// <summary>Gets or sets the genre (genre marks).</summary>
            public string? Genre { get; set; }

            /// <summary>Gets or sets when it was marked (UTC).</summary>
            public DateTime At { get; set; }
        }

        private sealed class Entry
        {
            public string Kind { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public DateTime At { get; set; }
        }

        private sealed class UserData
        {
            public Dictionary<string, Entry> Titles { get; set; } = new Dictionary<string, Entry>(StringComparer.Ordinal);

            public Dictionary<string, Entry> Genres { get; set; } = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
    }
}
