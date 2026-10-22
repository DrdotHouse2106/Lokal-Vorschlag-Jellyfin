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

namespace Jellyfin.Plugin.LocalRecs.Collections
{
    /// <summary>
    /// Zu welcher Filmreihe (TMDB-Sammlung) ein Film gehört – von TMDB (<c>movie/{id}</c> → <c>belongs_to_collection</c>),
    /// nach und nach für den ganzen Bestand abgefragt und lange zwischengespeichert. Gesendet wird nur die TMDB-ID des Films.
    /// Dazu Poster und Hintergrund der Reihe, auf dem Server zwischengespeichert, damit Geräte nichts bei TMDB laden.
    /// </summary>
    public class MovieSeriesLookup
    {
        /// <summary>Days a lookup is kept (also „belongs to no series“).</summary>
        public const int CacheDays = 60;

        /// <summary>Maximum lookups per run.</summary>
        public const int MaxLookupsPerRun = 400;

        /// <summary>Maximum size of a cached image.</summary>
        public const int MaxImageBytes = 3 * 1024 * 1024;

        private const string TmdbBase = "https://api.themoviedb.org/3/";
        private const string ImageBase = "https://image.tmdb.org/t/p/";
        private const int MaxConsecutiveFailures = 5;

        private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(100);

        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly ILogger<MovieSeriesLookup> _logger;
        private readonly string _cachePath;
        private readonly string _imageFolder;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _running = new SemaphoreSlim(1, 1);
        private CacheFile? _cache;
        private long _version;

        /// <summary>
        /// Initializes a new instance of the <see cref="MovieSeriesLookup"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="cachePath">File of the cache.</param>
        /// <param name="send">Sends a request (for tests).</param>
        /// <param name="delay">Waits between requests (for tests).</param>
        public MovieSeriesLookup(
            ILogger<MovieSeriesLookup> logger,
            string cachePath,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
            _imageFolder = Path.Combine(Path.GetDirectoryName(cachePath) ?? string.Empty, "filmreihen-bilder");
            _send = send ?? ((request, token) => SharedClient.SendAsync(request, token));
            _delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        /// <summary>Gets a number that changes whenever the cache changes.</summary>
        public long Version => Interlocked.Read(ref _version);

        /// <summary>
        /// Reads the series of a movie from TMDB's answer to <c>movie/{id}</c>.
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>The series, or null if the movie belongs to none.</returns>
        public static SeriesInfo? ParseMovie(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("belongs_to_collection", out var collection) || collection.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = collection.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return new SeriesInfo
            {
                Id = id,
                Name = collection.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                PosterPath = collection.TryGetProperty("poster_path", out var poster) && poster.ValueKind == JsonValueKind.String ? poster.GetString() : null,
                BackdropPath = collection.TryGetProperty("backdrop_path", out var backdrop) && backdrop.ValueKind == JsonValueKind.String ? backdrop.GetString() : null
            };
        }

        /// <summary>
        /// Gets the series of a movie from the cache.
        /// </summary>
        /// <param name="tmdbMovieId">TMDB ID of the movie.</param>
        /// <returns>The series ID, or null (unknown or none).</returns>
        public string? SeriesOf(string? tmdbMovieId)
        {
            if (string.IsNullOrEmpty(tmdbMovieId))
            {
                return null;
            }

            lock (_lock)
            {
                LoadCache();
                return _cache!.Movies.TryGetValue(tmdbMovieId, out var entry) ? entry.SeriesId : null;
            }
        }

        /// <summary>
        /// Gets name and images of a series from the cache.
        /// </summary>
        /// <param name="seriesId">TMDB collection ID.</param>
        /// <returns>The series, or null.</returns>
        public SeriesInfo? GetSeries(string seriesId)
        {
            lock (_lock)
            {
                LoadCache();
                return _cache!.Series.TryGetValue(seriesId, out var info) ? info : null;
            }
        }

        /// <summary>
        /// Looks up missing or outdated movies at TMDB (most important first, at most <see cref="MaxLookupsPerRun"/>).
        /// </summary>
        /// <param name="tmdbMovieIds">TMDB IDs of the library movies, most important first.</param>
        /// <param name="config">Plugin configuration (TMDB key).</param>
        /// <param name="language">Language for the series name (e.g. „de-DE“), optional.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of movies looked up.</returns>
        public async Task<int> RefreshAsync(IEnumerable<string> tmdbMovieIds, PluginConfiguration config, string? language, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tmdbMovieIds);
            ArgumentNullException.ThrowIfNull(config);
            var key = ExternalSimilarityService.ResolveTmdbKey(config);
            if (key == null || !await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            try
            {
                var now = DateTime.UtcNow;
                List<string> pending;
                lock (_lock)
                {
                    LoadCache();
                    pending = tmdbMovieIds
                        .Where(id => !string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
                        .Distinct()
                        .Where(id => !_cache!.Movies.TryGetValue(id, out var entry) || now - entry.Fetched > TimeSpan.FromDays(CacheDays))
                        .Take(MaxLookupsPerRun)
                        .ToList();
                }

                var done = 0;
                var failures = 0;
                foreach (var id in pending)
                {
                    if (failures >= MaxConsecutiveFailures)
                    {
                        break;
                    }

                    await _delay(RequestInterval, cancellationToken).ConfigureAwait(false);
                    var url = TmdbBase + "movie/" + id + (string.IsNullOrEmpty(language) ? "?" : "?language=" + Uri.EscapeDataString(language) + "&");
                    using var request = new HttpRequestMessage(HttpMethod.Get, key.Length > 40 ? url.TrimEnd('?', '&') : url + "api_key=" + Uri.EscapeDataString(key));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (key.Length > 40)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    }

                    try
                    {
                        using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                        SeriesInfo? series = null;
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            _logger.LogWarning("Filmreihen: TMDB lehnt den Schlüssel ab (HTTP {Status})", (int)response.StatusCode);
                            break;
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            series = ParseMovie(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        }
                        else if (response.StatusCode != HttpStatusCode.NotFound)
                        {
                            failures++;
                            continue;
                        }

                        Store(id, series, DateTime.UtcNow);
                        failures = 0;
                        done++;
                        if (done % 50 == 0)
                        {
                            SaveCache();
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                    {
                        failures++;
                        _logger.LogDebug("Filmreihen: TMDB-Abfrage fehlgeschlagen: {Message}", ex.Message);
                    }
                }

                if (done > 0)
                {
                    SaveCache();
                    _logger.LogInformation("Filmreihen: {Count} Filme bei TMDB nachgeschlagen, {Pending} offen", done, pending.Count - done);
                }

                return done;
            }
            finally
            {
                _running.Release();
            }
        }

        /// <summary>
        /// Notes the series of a movie (e.g. from TMDB or tests).
        /// </summary>
        /// <param name="tmdbMovieId">TMDB ID of the movie.</param>
        /// <param name="series">The series, or null for none.</param>
        /// <param name="fetched">Time of the lookup.</param>
        public void Store(string tmdbMovieId, SeriesInfo? series, DateTime fetched)
        {
            lock (_lock)
            {
                LoadCache();
                _cache!.Movies[tmdbMovieId] = new MovieEntry { SeriesId = series?.Id, Fetched = fetched };
                if (series != null)
                {
                    _cache.Series[series.Id] = series;
                }

                Interlocked.Increment(ref _version);
            }
        }

        /// <summary>
        /// Gets the poster or backdrop of a series (cached on the server, loaded from TMDB once).
        /// </summary>
        /// <param name="seriesId">TMDB collection ID (only known series).</param>
        /// <param name="backdrop">True for the backdrop, false for the poster.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Image bytes and content type, or null.</returns>
        public async Task<(byte[] Data, string ContentType)?> GetImageAsync(string seriesId, bool backdrop, CancellationToken cancellationToken)
        {
            var info = GetSeries(seriesId);
            var path = backdrop ? info?.BackdropPath : info?.PosterPath;
            if (info == null || string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var file = Path.Combine(_imageFolder, seriesId + (backdrop ? "-hintergrund" : "-poster") + Path.GetExtension(path));
            var contentType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            try
            {
                if (File.Exists(file))
                {
                    return (await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), contentType);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, ImageBase + (backdrop ? "w1280" : "w500") + path);
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes)
                {
                    return null;
                }

                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (data.Length == 0 || data.Length > MaxImageBytes)
                {
                    return null;
                }

                Directory.CreateDirectory(_imageFolder);
                await File.WriteAllBytesAsync(file, data, cancellationToken).ConfigureAwait(false);
                return (data, contentType);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is UnauthorizedAccessException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogDebug("Filmreihen: Bild nicht ladbar: {Message}", ex.Message);
                return null;
            }
        }

        private void SaveCache()
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
                _logger.LogWarning("Filmreihen konnten nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private void LoadCache()
        {
            if (_cache != null)
            {
                return;
            }

            _cache = new CacheFile();
            try
            {
                if (File.Exists(_cachePath))
                {
                    _cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath)) ?? new CacheFile();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Filmreihen konnten nicht gelesen werden: {Message}", ex.Message);
            }
        }

        /// <summary>A series (TMDB collection).</summary>
        public class SeriesInfo
        {
            /// <summary>Gets or sets the TMDB collection ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the TMDB poster path.</summary>
            public string? PosterPath { get; set; }

            /// <summary>Gets or sets the TMDB backdrop path.</summary>
            public string? BackdropPath { get; set; }
        }

        /// <summary>Lookup of a movie.</summary>
        public class MovieEntry
        {
            /// <summary>Gets or sets the series ID, or null for none.</summary>
            public string? SeriesId { get; set; }

            /// <summary>Gets or sets the time of the lookup.</summary>
            public DateTime Fetched { get; set; }
        }

        /// <summary>Content of the cache file.</summary>
        public class CacheFile
        {
            /// <summary>Gets or sets the lookups by TMDB movie ID.</summary>
            public Dictionary<string, MovieEntry> Movies { get; set; } = new Dictionary<string, MovieEntry>();

            /// <summary>Gets or sets the series by TMDB collection ID.</summary>
            public Dictionary<string, SeriesInfo> Series { get; set; } = new Dictionary<string, SeriesInfo>();
        }
    }
}
