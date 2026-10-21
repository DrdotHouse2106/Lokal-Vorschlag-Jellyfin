using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Banner
{
    /// <summary>
    /// Merkt sich je Benutzer den Banner der letzten Tage (nur Titel-IDs): damit alle Geräte am selben Tag dasselbe
    /// zeigen und kürzlich gezeigte Titel seltener wiederkommen. Höchstens 14 Tage.
    /// </summary>
    public class BannerStore
    {
        /// <summary>Days kept.</summary>
        public const int KeepDays = 14;

        private readonly ILogger<BannerStore> _logger;
        private readonly string _path;
        private readonly object _lock = new object();
        private Dictionary<string, Dictionary<string, List<Guid>>>? _users;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerStore"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="path">File of the store.</param>
        public BannerStore(ILogger<BannerStore> logger, string path)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>
        /// Gets the banner of a day, or null.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="day">The local day.</param>
        /// <returns>The title IDs, or null.</returns>
        public IReadOnlyList<Guid>? Get(Guid userId, DateOnly day)
        {
            lock (_lock)
            {
                Load();
                return _users!.TryGetValue(userId.ToString("N"), out var days) && days.TryGetValue(Key(day), out var ids) ? ids.ToList() : null;
            }
        }

        /// <summary>
        /// Gets how many days ago a title was last in the banner before the given day (1 = yesterday), or null.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="day">The current local day.</param>
        /// <returns>Days ago by title.</returns>
        public Dictionary<Guid, int> RecentlyShown(Guid userId, DateOnly day)
        {
            var result = new Dictionary<Guid, int>();
            lock (_lock)
            {
                Load();
                if (!_users!.TryGetValue(userId.ToString("N"), out var days))
                {
                    return result;
                }

                foreach (var (key, ids) in days)
                {
                    if (!DateOnly.TryParseExact(key, "yyyy-MM-dd", out var shown))
                    {
                        continue;
                    }

                    var ago = day.DayNumber - shown.DayNumber;
                    if (ago < 1 || ago > KeepDays)
                    {
                        continue;
                    }

                    foreach (var id in ids)
                    {
                        if (!result.TryGetValue(id, out var known) || ago < known)
                        {
                            result[id] = ago;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Stores the banner of a day (drops days older than <see cref="KeepDays"/>).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="day">The local day.</param>
        /// <param name="ids">The title IDs.</param>
        public void Set(Guid userId, DateOnly day, IReadOnlyList<Guid> ids)
        {
            ArgumentNullException.ThrowIfNull(ids);
            string json;
            lock (_lock)
            {
                Load();
                var userKey = userId.ToString("N");
                if (!_users!.TryGetValue(userKey, out var days))
                {
                    _users[userKey] = days = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
                }

                days[Key(day)] = ids.ToList();
                foreach (var old in days.Keys.Where(k => !DateOnly.TryParseExact(k, "yyyy-MM-dd", out var d) || day.DayNumber - d.DayNumber > KeepDays).ToList())
                {
                    days.Remove(old);
                }

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
                _logger.LogWarning("Banner konnte nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        private void Load()
        {
            if (_users != null)
            {
                return;
            }

            _users = new Dictionary<string, Dictionary<string, List<Guid>>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<Guid>>>>(File.ReadAllText(_path));
                    if (loaded != null)
                    {
                        _users = new Dictionary<string, Dictionary<string, List<Guid>>>(loaded, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Banner-Verlauf konnte nicht gelesen werden: {Message}", ex.Message);
            }
        }
    }
}
