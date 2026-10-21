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
    /// zeigen und kürzlich gezeigte Titel seltener wiederkommen. Höchstens 14 Tage. Dazu die Titel, die der Benutzer mit
    /// „Später“ für einige Wochen aus dem Banner genommen hat (eigene Datei, ohne Wertung für die Empfehlungen).
    /// </summary>
    public class BannerStore
    {
        /// <summary>Days kept.</summary>
        public const int KeepDays = 14;

        private readonly ILogger<BannerStore> _logger;
        private readonly string _path;
        private readonly string _laterPath;
        private readonly object _lock = new object();
        private Dictionary<string, Dictionary<string, List<Guid>>>? _users;
        private Dictionary<string, Dictionary<Guid, DateTime>>? _later;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerStore"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="path">File of the store.</param>
        public BannerStore(ILogger<BannerStore> logger, string path)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _laterPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "-spaeter.json");
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
        /// Gets on how many of the last days (before the given day) each title was in the banner.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="day">The current local day.</param>
        /// <param name="days">Days looked back.</param>
        /// <returns>Number of days by title.</returns>
        public Dictionary<Guid, int> ShownDays(Guid userId, DateOnly day, int days)
        {
            var result = new Dictionary<Guid, int>();
            lock (_lock)
            {
                Load();
                if (!_users!.TryGetValue(userId.ToString("N"), out var stored))
                {
                    return result;
                }

                foreach (var (key, ids) in stored)
                {
                    if (DateOnly.TryParseExact(key, "yyyy-MM-dd", out var shown) && day.DayNumber - shown.DayNumber is var ago && ago >= 1 && ago <= days)
                    {
                        foreach (var id in ids.Distinct())
                        {
                            result[id] = result.GetValueOrDefault(id) + 1;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Takes a title out of the banner of a user until a time („Später“) – it stays in the recommendations.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="itemId">The title.</param>
        /// <param name="until">Until when (UTC).</param>
        public void SetLater(Guid userId, Guid itemId, DateTime until)
        {
            ChangeLater(userId, later => later[itemId] = until);
        }

        /// <summary>
        /// Brings a title back into the banner.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="itemId">The title.</param>
        public void RemoveLater(Guid userId, Guid itemId)
        {
            ChangeLater(userId, later => later.Remove(itemId));
        }

        /// <summary>
        /// Gets the titles a user took out of the banner, with the time until when (only running ones).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>Until when by title.</returns>
        public Dictionary<Guid, DateTime> GetLater(Guid userId, DateTime now)
        {
            lock (_lock)
            {
                LoadLater();
                return _later!.TryGetValue(userId.ToString("N"), out var later)
                    ? later.Where(e => e.Value > now).ToDictionary(e => e.Key, e => e.Value)
                    : new Dictionary<Guid, DateTime>();
            }
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

            Write(_path, json, _logger);
        }

        private static void Write(string path, string json, ILogger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                logger.LogWarning("Banner konnte nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        private void ChangeLater(Guid userId, Action<Dictionary<Guid, DateTime>> change)
        {
            string json;
            lock (_lock)
            {
                LoadLater();
                var key = userId.ToString("N");
                if (!_later!.TryGetValue(key, out var later))
                {
                    _later[key] = later = new Dictionary<Guid, DateTime>();
                }

                change(later);
                var now = DateTime.UtcNow;
                foreach (var expired in later.Where(e => e.Value <= now).Select(e => e.Key).ToList())
                {
                    later.Remove(expired);
                }

                json = JsonSerializer.Serialize(_later);
            }

            Write(_laterPath, json, _logger);
        }

        private void LoadLater()
        {
            if (_later != null)
            {
                return;
            }

            _later = new Dictionary<string, Dictionary<Guid, DateTime>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_laterPath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<Guid, DateTime>>>(File.ReadAllText(_laterPath));
                    if (loaded != null)
                    {
                        _later = new Dictionary<string, Dictionary<Guid, DateTime>>(loaded, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("„Später“-Liste des Banners konnte nicht gelesen werden: {Message}", ex.Message);
            }
        }

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
