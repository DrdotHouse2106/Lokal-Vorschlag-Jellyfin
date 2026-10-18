using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Lädt die gepflegte Welten-Liste (<c>welten.json</c>, Standard: im GitHub-Repository des Plugins) einmal täglich
    /// im Hintergrund. Der letzte funktionierende Stand wird lokal gespeichert und weiter genutzt, wenn die Quelle
    /// nicht erreichbar oder fehlerhaft ist.
    /// </summary>
    public class WorldListSource
    {
        /// <summary>
        /// Default list: maintained in the plugin repository.
        /// </summary>
        public const string DefaultUrl = "https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/welten/welten.json";

        private static readonly TimeSpan ReloadInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

        private readonly ILogger<WorldListSource> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly string _cachePath;
        private readonly object _lock = new object();
        private IReadOnlyList<WorldDefinition>? _worlds;
        private string? _loadedSource;
        private DateTime _lastAttempt = DateTime.MinValue;
        private bool _loading;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldListSource"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="cachePath">File for the last good state.</param>
        /// <param name="httpClientFactory">HTTP client factory, optional.</param>
        public WorldListSource(ILogger<WorldListSource> logger, string cachePath, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>Gets the time the list was last loaded successfully (UTC).</summary>
        public DateTime? LastLoaded { get; private set; }

        /// <summary>Gets the error of the last load attempt, if any.</summary>
        public string? LastError { get; private set; }

        /// <summary>Gets a value indicating whether the worlds come from the local copy (source not reachable yet).</summary>
        public bool FromCache { get; private set; }

        /// <summary>Gets a counter that changes whenever a new list was loaded (for caches).</summary>
        public int Version { get; private set; }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the worlds of the configured list. Starts a (re)load in the background if needed.
        /// </summary>
        /// <returns>The worlds, or null if no list is configured or none could be loaded yet.</returns>
        public IReadOnlyList<WorldDefinition>? GetWorlds()
        {
            var source = Config.WorldListSource?.Trim();
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var start = false;
            lock (_lock)
            {
                if (_worlds == null && !string.Equals(_loadedSource, source, StringComparison.Ordinal))
                {
                    LoadCache(source);
                }

                var due = !string.Equals(_loadedSource, source, StringComparison.Ordinal) || FromCache
                    ? now - _lastAttempt >= RetryInterval
                    : now - _lastAttempt >= ReloadInterval;
                if (!_loading && due)
                {
                    _loading = true;
                    _lastAttempt = now;
                    start = true;
                }
            }

            if (start)
            {
                _ = Task.Run(() => LoadAsync(source));
            }

            lock (_lock)
            {
                return _worlds;
            }
        }

        /// <summary>
        /// Reloads the list now (e.g. after editing it on GitHub).
        /// </summary>
        /// <returns>A task.</returns>
        public Task ReloadAsync()
        {
            var source = Config.WorldListSource?.Trim();
            if (string.IsNullOrEmpty(source))
            {
                return Task.CompletedTask;
            }

            lock (_lock)
            {
                _loading = true;
                _lastAttempt = DateTime.UtcNow;
            }

            return LoadAsync(source);
        }

        private static bool IsUrl(string source) =>
            source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private void LoadCache(string source)
        {
            try
            {
                if (!File.Exists(_cachePath))
                {
                    return;
                }

                var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath));
                if (cache?.Json != null && string.Equals(cache.Source, source, StringComparison.Ordinal))
                {
                    _worlds = WorldDefinition.ParseJson(cache.Json);
                    LastLoaded = cache.Loaded;
                    FromCache = true;
                    Version++;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Zwischengespeicherte Welten-Liste konnte nicht gelesen werden");
            }
        }

        private async Task LoadAsync(string source)
        {
            try
            {
                string json;
                if (IsUrl(source))
                {
                    var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    json = await client.GetStringAsync(new Uri(source), CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    json = await File.ReadAllTextAsync(source).ConfigureAwait(false);
                }

                var worlds = WorldDefinition.ParseJson(json);
                var now = DateTime.UtcNow;
                lock (_lock)
                {
                    _worlds = worlds;
                    _loadedSource = source;
                    LastLoaded = now;
                    LastError = null;
                    FromCache = false;
                    Version++;
                }

                SaveCache(source, json, now);
                _logger.LogInformation("Welten-Liste geladen: {Count} Welten ({Source})", worlds.Count, source);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is UnauthorizedAccessException || ex is ArgumentException || ex is UriFormatException || ex is NotSupportedException)
            {
                lock (_lock)
                {
                    LastError = ex.Message;
                }

                _logger.LogWarning("Welten-Liste konnte nicht geladen werden ({Source}): {Message}", source, ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    _loading = false;
                }
            }
        }

        private void SaveCache(string source, string json, DateTime loaded)
        {
            try
            {
                var directory = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_cachePath, JsonSerializer.Serialize(new CacheFile { Source = source, Json = json, Loaded = loaded }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Welten-Liste konnte nicht zwischengespeichert werden");
            }
        }

        private sealed class CacheFile
        {
            public string? Source { get; set; }

            public string? Json { get; set; }

            public DateTime Loaded { get; set; }
        }
    }
}
