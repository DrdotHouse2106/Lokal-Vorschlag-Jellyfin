using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// Holt öffentliche Ähnlichkeitslisten (TMDB „recommendations“ und „similar“, optional Trakt „related“) für die
    /// Titel der Bibliothek und hält sie im Zwischenspeicher. Gesendet wird nur die Titel-ID – keine Benutzer, kein
    /// Sehverlauf. Abgefragt wird nach und nach die ganze Bibliothek (gesehene Titel zuerst), damit die Anfragen
    /// den Bestand abbilden und nicht, was geschaut wurde. Ohne Schlüssel oder bei Fehlern bleibt es still weg.
    /// </summary>
    public class ExternalSimilarityService
    {
        /// <summary>Key source: own setting.</summary>
        public const string KeyOwn = "eigen";

        /// <summary>Key source: key in Jellyfin's TMDb settings.</summary>
        public const string KeyJellyfinSetting = "jellyfin-einstellung";

        /// <summary>Key source: Jellyfin's built-in key.</summary>
        public const string KeyJellyfinBuiltIn = "jellyfin-eingebaut";

        private const string TmdbBase = "https://api.themoviedb.org/3/";
        private const string TraktBase = "https://api.trakt.tv/";
        private const int MaxConsecutiveFailures = 5;

        // Pause zwischen zwei Anfragen (TMDB und Trakt erlauben weit mehr; so bleibt der Server ruhig)
        private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(260);

        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly ILogger<ExternalSimilarityService> _logger;
        private readonly string _cachePath;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _running = new SemaphoreSlim(1, 1);
        private Dictionary<string, CacheEntry>? _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalSimilarityService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="cachePath">File of the cache.</param>
        /// <param name="send">Sends a request (for tests); default: a shared HTTP client without request logging.</param>
        /// <param name="delay">Waits between requests (for tests).</param>
        public ExternalSimilarityService(
            ILogger<ExternalSimilarityService> logger,
            string cachePath,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
            _send = send ?? ((request, token) => SharedClient.SendAsync(request, token));
            _delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        private enum Source
        {
            Tmdb,
            Trakt
        }

        /// <summary>
        /// Gets the TMDB key to use: own setting, otherwise (if allowed) the key of Jellyfin's TMDb plugin (its setting or its built-in key).
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>The key, or null.</returns>
        public static string? ResolveTmdbKey(PluginConfiguration config) => ResolveTmdbKeyWithSource(config).Key;

        /// <summary>
        /// Gets the TMDB key and where it comes from (for the settings page).
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Key and source: "eigen", "jellyfin-einstellung", "jellyfin-eingebaut" or null.</returns>
        public static (string? Key, string? Source) ResolveTmdbKeyWithSource(PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (!string.IsNullOrWhiteSpace(config.TmdbApiKey))
            {
                return (config.TmdbApiKey.Trim(), KeyOwn);
            }

            if (!config.UseJellyfinTmdbKey)
            {
                return (null, null);
            }

            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "MediaBrowser.Providers", StringComparison.Ordinal));
                if (assembly == null)
                {
                    return (null, null);
                }

                var pluginType = assembly.GetType("MediaBrowser.Providers.Plugins.Tmdb.Plugin");
                var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var configuration = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
                var own = configuration?.GetType().GetProperty("TmdbApiKey")?.GetValue(configuration) as string;
                if (!string.IsNullOrWhiteSpace(own))
                {
                    return (own.Trim(), KeyJellyfinSetting);
                }

                var builtIn = assembly.GetType("MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils")
                    ?.GetField("ApiKey", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                return string.IsNullOrWhiteSpace(builtIn) ? (null, null) : (builtIn, KeyJellyfinBuiltIn);
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is InvalidOperationException || ex is NotSupportedException
                || ex is AmbiguousMatchException || ex is TypeLoadException || ex is FileNotFoundException || ex is BadImageFormatException)
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Gets the number of cached lists and the newest fetch time (for the settings page).
        /// </summary>
        /// <returns>Count and newest fetch time.</returns>
        public (int Count, DateTime? Newest) CacheInfo()
        {
            lock (_lock)
            {
                LoadCache();
                return (_cache!.Count, _cache.Count == 0 ? null : _cache.Values.Max(e => e.Fetched));
            }
        }

        /// <summary>
        /// Gets the similar titles of a title from the cache (also if outdated – better than nothing).
        /// </summary>
        /// <param name="kind">"movie" or "tv".</param>
        /// <param name="tmdbId">TMDB ID.</param>
        /// <returns>The titles, or null if never fetched.</returns>
        public IReadOnlyList<SimilarTitle>? Lookup(string kind, string tmdbId)
        {
            lock (_lock)
            {
                LoadCache();
                return _cache!.TryGetValue(SimilarTitle.Key(kind, tmdbId), out var entry) ? entry.Titles : null;
            }
        }

        /// <summary>
        /// Fetches missing or outdated lists for the given titles (in this order, at most the configured number per run).
        /// </summary>
        /// <param name="items">Library titles, most important first.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>What was done.</returns>
        public async Task<RefreshResult> RefreshAsync(IEnumerable<MediaItemMetadata> items, PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(config);
            var result = new RefreshResult();
            var tmdbKey = ResolveTmdbKey(config);
            var traktId = string.IsNullOrWhiteSpace(config.TraktClientId) ? null : config.TraktClientId.Trim();
            if (tmdbKey == null && traktId == null)
            {
                result.Skipped = "kein TMDB-Schlüssel und keine Trakt-Client-ID";
                return result;
            }

            if (!await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                result.Skipped = "läuft bereits";
                return result;
            }

            try
            {
                var maxAge = TimeSpan.FromDays(Math.Max(1, config.ExternalSimilarityCacheDays));
                var now = DateTime.UtcNow;
                List<MediaItemMetadata> pending;
                lock (_lock)
                {
                    LoadCache();
                    pending = items
                        .Where(i => !string.IsNullOrEmpty(i.TmdbId))
                        .GroupBy(i => SimilarTitle.Key(ExternalSimilarityScorer.KindOf(i.Type), i.TmdbId!))
                        .Select(g => g.First())
                        .Where(i => !_cache!.TryGetValue(SimilarTitle.Key(ExternalSimilarityScorer.KindOf(i.Type), i.TmdbId!), out var entry)
                            || now - entry.Fetched > maxAge)
                        .Take(Math.Max(0, config.ExternalSimilarityMaxLookupsPerRun))
                        .ToList();
                }

                var sources = new SourceState { TmdbKey = tmdbKey, TraktId = traktId };
                foreach (var item in pending)
                {
                    if (sources.ConsecutiveFailures >= MaxConsecutiveFailures || (sources.TmdbKey == null && sources.TraktId == null))
                    {
                        result.Aborted = true;
                        break;
                    }

                    var kind = ExternalSimilarityScorer.KindOf(item.Type);
                    var lists = new List<(IReadOnlyList<string> Ids, double SourceWeight)>();
                    var complete = true;
                    if (sources.TmdbKey != null)
                    {
                        complete &= await AddAsync(lists, sources, Source.Tmdb, TmdbBase + kind + "/" + item.TmdbId + "/recommendations?page=1", SimilarityParser.TmdbRecommendationsWeight, result, cancellationToken).ConfigureAwait(false);
                        complete &= await AddAsync(lists, sources, Source.Tmdb, TmdbBase + kind + "/" + item.TmdbId + "/similar?page=1", SimilarityParser.TmdbSimilarWeight, result, cancellationToken).ConfigureAwait(false);
                    }

                    if (sources.TraktId != null && !string.IsNullOrEmpty(item.ImdbId))
                    {
                        var path = (kind == "tv" ? "shows/" : "movies/") + Uri.EscapeDataString(item.ImdbId) + "/related?limit=20";
                        complete &= await AddAsync(lists, sources, Source.Trakt, TraktBase + path, SimilarityParser.TraktRelatedWeight, result, cancellationToken).ConfigureAwait(false);
                    }

                    // Nur vollständige Antworten speichern – bei vorübergehenden Fehlern beim nächsten Lauf erneut
                    if (!complete)
                    {
                        continue;
                    }

                    var titles = SimilarityParser.Merge(kind, item.TmdbId!, lists);
                    lock (_lock)
                    {
                        _cache![SimilarTitle.Key(kind, item.TmdbId!)] = new CacheEntry { Fetched = DateTime.UtcNow, Titles = titles };
                    }

                    result.Fetched++;
                    if (result.Fetched % 50 == 0)
                    {
                        SaveCache();
                    }
                }

                result.Pending = pending.Count - result.Fetched;
                Prune(maxAge);
                SaveCache();
                return result;
            }
            finally
            {
                _running.Release();
            }
        }

        private static bool IsBearer(string key) => key.Length > 40;

        private static Uri BuildUri(string url, Source source, SourceState sources)
        {
            if (source == Source.Tmdb && !IsBearer(sources.TmdbKey!))
            {
                url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api_key=" + Uri.EscapeDataString(sources.TmdbKey!);
            }

            return new Uri(url);
        }

        private async Task<bool> AddAsync(
            List<(IReadOnlyList<string> Ids, double SourceWeight)> lists,
            SourceState sources,
            Source source,
            string url,
            double weight,
            RefreshResult result,
            CancellationToken cancellationToken)
        {
            if ((source == Source.Tmdb ? sources.TmdbKey : sources.TraktId) == null)
            {
                return false;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                await _delay(RequestInterval, cancellationToken).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(url, source, sources));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd("Quallenkino/" + (typeof(ExternalSimilarityService).Assembly.GetName().Version?.ToString() ?? "1"));
                if (source == Source.Trakt)
                {
                    request.Headers.Add("trakt-api-version", "2");
                    request.Headers.Add("trakt-api-key", sources.TraktId);
                }
                else if (IsBearer(sources.TmdbKey!))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sources.TmdbKey);
                }

                try
                {
                    result.Requests++;
                    using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Titel dort unbekannt – leere Liste ist eine gültige Antwort
                        sources.ConsecutiveFailures = 0;
                        return true;
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning(
                            "Ähnliche Titel: {Source} lehnt den Schlüssel ab (HTTP {Status}) – diese Quelle bleibt in diesem Lauf aus",
                            source == Source.Tmdb ? "TMDB" : "Trakt",
                            (int)response.StatusCode);
                        if (source == Source.Tmdb)
                        {
                            sources.TmdbKey = null;
                        }
                        else
                        {
                            sources.TraktId = null;
                        }

                        result.Errors++;
                        return false;
                    }

                    if (response.StatusCode == (HttpStatusCode)429 && attempt == 0)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                        await _delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        sources.ConsecutiveFailures++;
                        result.Errors++;
                        return false;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    lists.Add((source == Source.Tmdb ? SimilarityParser.ParseTmdb(json) : SimilarityParser.ParseTrakt(json), weight));
                    sources.ConsecutiveFailures = 0;
                    return true;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    sources.ConsecutiveFailures++;
                    result.Errors++;
                    _logger.LogDebug("Ähnliche Titel: Anfrage an {Source} fehlgeschlagen: {Message}", source == Source.Tmdb ? "TMDB" : "Trakt", ex.Message);
                    return false;
                }
            }

            sources.ConsecutiveFailures++;
            result.Errors++;
            return false;
        }

        private void Prune(TimeSpan maxAge)
        {
            var limit = DateTime.UtcNow - TimeSpan.FromTicks(maxAge.Ticks * 4);
            lock (_lock)
            {
                foreach (var key in _cache!.Where(e => e.Value.Fetched < limit).Select(e => e.Key).ToList())
                {
                    _cache!.Remove(key);
                }
            }
        }

        private void LoadCache()
        {
            if (_cache != null)
            {
                return;
            }

            _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(_cachePath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath));
                    if (loaded != null)
                    {
                        _cache = new Dictionary<string, CacheEntry>(loaded, StringComparer.Ordinal);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Zwischenspeicher der ähnlichen Titel konnte nicht gelesen werden");
            }
        }

        private void SaveCache()
        {
            string json;
            lock (_lock)
            {
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
                _logger.LogWarning("Zwischenspeicher der ähnlichen Titel konnte nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// What a refresh did.
        /// </summary>
        public class RefreshResult
        {
            /// <summary>Gets or sets the number of titles fetched completely.</summary>
            public int Fetched { get; set; }

            /// <summary>Gets or sets the number of titles still missing or outdated after the run.</summary>
            public int Pending { get; set; }

            /// <summary>Gets or sets the number of requests sent.</summary>
            public int Requests { get; set; }

            /// <summary>Gets or sets the number of failed requests.</summary>
            public int Errors { get; set; }

            /// <summary>Gets or sets a value indicating whether the run stopped early after repeated errors.</summary>
            public bool Aborted { get; set; }

            /// <summary>Gets or sets why nothing was fetched, if so.</summary>
            public string? Skipped { get; set; }
        }

        /// <summary>
        /// A cached list.
        /// </summary>
        public class CacheEntry
        {
            /// <summary>Gets or sets the time of fetching (UTC).</summary>
            public DateTime Fetched { get; set; }

            /// <summary>Gets or sets the similar titles.</summary>
            public List<SimilarTitle> Titles { get; set; } = new List<SimilarTitle>();
        }

        private sealed class SourceState
        {
            public string? TmdbKey { get; set; }

            public string? TraktId { get; set; }

            public int ConsecutiveFailures { get; set; }
        }
    }
}
