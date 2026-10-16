using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Hält die zuletzt berechneten Empfehlungen pro Benutzer und speichert sie als JSON-Datei,
    /// damit sie nach einem Neustart sofort wieder auf der Startseite erscheinen.
    /// </summary>
    public class RecommendationStore
    {
        private readonly ILogger<RecommendationStore> _logger;
        private readonly string _filePath;
        private readonly object _lock = new object();
        private Dictionary<Guid, UserRecommendations> _recommendations = new Dictionary<Guid, UserRecommendations>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationStore"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="filePath">Path of the JSON file used for persistence.</param>
        public RecommendationStore(ILogger<RecommendationStore> logger, string filePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Load();
        }

        /// <summary>
        /// Gets the time of the last update (UTC), or null if no recommendations were computed yet.
        /// </summary>
        public DateTime? LastUpdated { get; private set; }

        /// <summary>
        /// Gets the number of users with stored recommendations.
        /// </summary>
        public int UserCount
        {
            get
            {
                lock (_lock)
                {
                    return _recommendations.Count;
                }
            }
        }

        /// <summary>
        /// Gets the recommendations of a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <returns>The recommendations, or null if none are stored.</returns>
        public UserRecommendations? Get(Guid userId)
        {
            lock (_lock)
            {
                return _recommendations.TryGetValue(userId, out var recs) ? recs : null;
            }
        }

        /// <summary>
        /// Replaces all stored recommendations and writes them to disk.
        /// </summary>
        /// <param name="recommendations">Recommendations per user.</param>
        public void ReplaceAll(IDictionary<Guid, UserRecommendations> recommendations)
        {
            ArgumentNullException.ThrowIfNull(recommendations);

            lock (_lock)
            {
                _recommendations = new Dictionary<Guid, UserRecommendations>(recommendations);
                LastUpdated = DateTime.UtcNow;
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                var data = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_filePath));
                if (data?.Users != null)
                {
                    _recommendations = data.Users.ToDictionary(kv => kv.Key, kv => kv.Value);
                    LastUpdated = data.LastUpdated;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Gespeicherte Empfehlungen konnten nicht gelesen werden: {Path}", _filePath);
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

                var data = new StoreFile { LastUpdated = LastUpdated, Users = _recommendations };
                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(data));
                File.Move(tempPath, _filePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Empfehlungen konnten nicht gespeichert werden: {Path}", _filePath);
            }
        }

        private sealed class StoreFile
        {
            public DateTime? LastUpdated { get; set; }

            public Dictionary<Guid, UserRecommendations>? Users { get; set; }
        }
    }
}
