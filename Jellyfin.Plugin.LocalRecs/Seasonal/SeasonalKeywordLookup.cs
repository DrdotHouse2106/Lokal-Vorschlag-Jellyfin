using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Seasonal
{
    /// <summary>
    /// TMDB-Stichwörter der Filme und Serien (z. B. „christmas“, „halloween“) für „Passend zur Jahreszeit“ – nach und nach
    /// für den ganzen Bestand abgefragt und lange zwischengespeichert. Gesendet wird nur die TMDB-ID des Titels.
    /// </summary>
    public class SeasonalKeywordLookup
    {
        /// <summary>Days a lookup is kept.</summary>
        public const int CacheDays = 180;

        /// <summary>Maximum lookups per run.</summary>
        public const int MaxLookupsPerRun = 400;

        private const string TmdbBase = "https://api.themoviedb.org/3/";
        private const int MaxConsecutiveFailures = 5;

        private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(100);

        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly ILogger<SeasonalKeywordLookup> _logger;
        private readonly string _cachePath;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _running = new SemaphoreSlim(1, 1);
        private Dictionary<string, Entry>? _cache;
        private long _version;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeasonalKeywordLookup"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="cachePath">File of the cache.</param>
        /// <param name="send">Sends a request (for tests).</param>
        /// <param name="delay">Waits between requests (for tests).</param>
        public SeasonalKeywordLookup(
            ILogger<SeasonalKeywordLookup> logger,
            string cachePath,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
            _send = send ?? ((request, token) => SharedClient.SendAsync(request, token));
            _delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        /// <summary>Gets a number that changes whenever the cache changes.</summary>
        public long Version => Interlocked.Read(ref _version);

        /// <summary>
        /// Gets the cache key of a title: „movie:603“ or „tv:1399“.
        /// </summary>
        /// <param name="isSeries">True for a series.</param>
        /// <param name="tmdbId">TMDB ID.</param>
        /// <returns>The key.</returns>
        public static string Key(bool isSeries, string tmdbId) => (isSeries ? "tv:" : "movie:") + tmdbId;

        /// <summary>
        /// Reads the keyword names from TMDB's answer (movies: „keywords“, series: „results“).
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>Keyword names in lower case.</returns>
        public static List<string> ParseKeywords(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var list = root.TryGetProperty("keywords", out var keywords) && keywords.ValueKind == JsonValueKind.Array ? keywords
                : root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array ? results : default;
            if (list.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return list.EnumerateArray()
                .Select(k => k.TryGetProperty("name", out var name) ? name.GetString() : null)
                .OfType<string>()
                .Select(n => n.Trim().ToLowerInvariant())
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Gets the keywords of a title from the cache.
        /// </summary>
        /// <param name="key">Cache key (see <see cref="Key"/>).</param>
        /// <returns>The keywords, or null if not looked up yet.</returns>
        public IReadOnlyList<string>? Get(string key)
        {
            lock (_lock)
            {
                Load();
                return _cache!.TryGetValue(key, out var entry) ? entry.Keywords : null;
            }
        }

        /// <summary>
        /// Notes the keywords of a title.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="keywords">Keyword names.</param>
        /// <param name="fetched">Time of the lookup.</param>
        public void Store(string key, IReadOnlyList<string> keywords, DateTime fetched)
        {
            lock (_lock)
            {
                Load();
                _cache![key] = new Entry { Keywords = keywords.ToList(), Fetched = fetched };
                Interlocked.Increment(ref _version);
            }
        }

        /// <summary>
        /// Looks up missing or outdated titles at TMDB (at most <see cref="MaxLookupsPerRun"/>).
        /// </summary>
        /// <param name="keys">Cache keys of the library titles (see <see cref="Key"/>).</param>
        /// <param name="config">Plugin configuration (TMDB key).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of titles looked up.</returns>
        public async Task<int> RefreshAsync(IEnumerable<string> keys, PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(config);
            var apiKey = ExternalSimilarityService.ResolveTmdbKey(config);
            if (apiKey == null || !await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            try
            {
                var now = DateTime.UtcNow;
                List<string> pending;
                lock (_lock)
                {
                    Load();
                    pending = keys.Distinct()
                        .Where(k => !_cache!.TryGetValue(k, out var entry) || now - entry.Fetched > TimeSpan.FromDays(CacheDays))
                        .Take(MaxLookupsPerRun)
                        .ToList();
                }

                var done = 0;
                var failures = 0;
                foreach (var key in pending)
                {
                    var parts = key.Split(':');
                    if (parts.Length != 2 || parts[1].Length == 0 || !parts[1].All(char.IsDigit))
                    {
                        continue;
                    }

                    if (failures >= MaxConsecutiveFailures)
                    {
                        break;
                    }

                    await _delay(RequestInterval, cancellationToken).ConfigureAwait(false);
                    var url = TmdbBase + parts[0] + "/" + parts[1] + "/keywords";
                    using var request = new HttpRequestMessage(HttpMethod.Get, apiKey.Length > 40 ? url : url + "?api_key=" + Uri.EscapeDataString(apiKey));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (apiKey.Length > 40)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    }

                    try
                    {
                        using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            _logger.LogWarning("Jahreszeit: TMDB lehnt den Schlüssel ab (HTTP {Status})", (int)response.StatusCode);
                            break;
                        }

                        List<string> keywords;
                        if (response.IsSuccessStatusCode)
                        {
                            keywords = ParseKeywords(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        }
                        else if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            keywords = new List<string>();
                        }
                        else
                        {
                            failures++;
                            continue;
                        }

                        Store(key, keywords, DateTime.UtcNow);
                        failures = 0;
                        done++;
                        if (done % 50 == 0)
                        {
                            Save();
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                    {
                        failures++;
                        _logger.LogDebug("Jahreszeit: TMDB-Abfrage fehlgeschlagen: {Message}", ex.Message);
                    }
                }

                if (done > 0)
                {
                    Save();
                    _logger.LogInformation("Jahreszeit: Stichwörter für {Count} Titel bei TMDB nachgeschlagen, {Pending} offen", done, pending.Count - done);
                }

                return done;
            }
            finally
            {
                _running.Release();
            }
        }

        private void Save()
        {
            string json;
            lock (_lock)
            {
                if (_cache == null)
                {
                    return;
                }

                json = JsonSerializer.Serialize(_cache);
            }

            try
            {
                var directory = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = _cachePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _cachePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Stichwörter konnten nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private void Load()
        {
            if (_cache != null)
            {
                return;
            }

            _cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(_cachePath))
                {
                    _cache = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_cachePath)) ?? _cache;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Stichwörter konnten nicht gelesen werden: {Message}", ex.Message);
            }
        }

        /// <summary>Keywords of a title.</summary>
        public class Entry
        {
            /// <summary>Gets or sets the keyword names (lower case).</summary>
            public List<string> Keywords { get; set; } = new List<string>();

            /// <summary>Gets or sets the time of the lookup.</summary>
            public DateTime Fetched { get; set; }
        }
    }
}
