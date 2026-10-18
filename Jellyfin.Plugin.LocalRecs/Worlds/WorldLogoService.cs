using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Holt die Logos der Welten (Link aus der Welten-Liste, meist ein TMDB-Firmenlogo) und speichert sie auf dem
    /// Server zwischen, damit Apps sie ohne eigenen Internetzugriff vom Jellyfin-Server laden können.
    /// Erlaubt sind nur https-Adressen und Bilddateien bis 2 MB; bei Dateien neben einer lokalen Liste nur
    /// Bilder innerhalb ihres Ordners.
    /// </summary>
    public class WorldLogoService
    {
        /// <summary>Maximum logo size in bytes.</summary>
        public const int MaxBytes = 2 * 1024 * 1024;

        private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);
        private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);
        private static readonly System.Text.RegularExpressions.Regex FanartPattern =
            new System.Text.RegularExpressions.Regex("^fanart:(collection|movie|tv)/([0-9]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg", ".gif" };

        private readonly ILogger<WorldLogoService> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly string _cacheDirectory;
        private readonly ConcurrentDictionary<string, Logo> _memory = new ConcurrentDictionary<string, Logo>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _failed = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _fetchLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldLogoService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="cacheDirectory">Folder for cached logos.</param>
        /// <param name="httpClientFactory">HTTP client factory, optional.</param>
        public WorldLogoService(ILogger<WorldLogoService> logger, string cacheDirectory, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Resolves the logo of a world to an https URL or a local image file.
        /// </summary>
        /// <param name="logo">Logo entry of the world list.</param>
        /// <param name="listSource">The world list (URL or file path).</param>
        /// <param name="fanartAvailable">Whether a fanart.tv API key is configured.</param>
        /// <returns>The resolved source, or null if it is not allowed.</returns>
        public static string? Resolve(string? logo, string? listSource, bool fanartAvailable = false)
        {
            if (string.IsNullOrWhiteSpace(logo))
            {
                return null;
            }

            logo = logo.Trim();
            if (logo.StartsWith("fanart:", StringComparison.OrdinalIgnoreCase))
            {
                return fanartAvailable && FanartPattern.IsMatch(logo) ? logo.ToLowerInvariant() : null;
            }

            if (Uri.TryCreate(logo, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps || absolute.IsFile))
            {
                return absolute.Scheme == Uri.UriSchemeHttps ? absolute.ToString() : null;
            }

            if (string.IsNullOrWhiteSpace(listSource) || Path.IsPathRooted(logo))
            {
                return null;
            }

            if (Uri.TryCreate(listSource, UriKind.Absolute, out var listUri) && (listUri.Scheme == Uri.UriSchemeHttp || listUri.Scheme == Uri.UriSchemeHttps))
            {
                var combined = new Uri(listUri, logo);
                return combined.Scheme == Uri.UriSchemeHttps ? combined.ToString() : null;
            }

            // Lokale Liste: nur Bilder innerhalb ihres Ordners
            var directory = Path.GetDirectoryName(Path.GetFullPath(listSource));
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var path = Path.GetFullPath(Path.Combine(directory, logo));
            var inside = path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            return inside && ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) ? path : null;
        }

        /// <summary>
        /// Resolves all logo sources of a world, in order of preference.
        /// </summary>
        /// <param name="logos">Logo entries of the world list.</param>
        /// <param name="listSource">The world list (URL or file path).</param>
        /// <param name="fanartAvailable">Whether a fanart.tv API key is configured.</param>
        /// <returns>The usable sources.</returns>
        public static IReadOnlyList<string> ResolveAll(IEnumerable<string>? logos, string? listSource, bool fanartAvailable)
        {
            return (logos ?? Enumerable.Empty<string>())
                .Select(l => Resolve(l, listSource, fanartAvailable))
                .Where(l => l != null)
                .Select(l => l!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Picks the best logo from a fanart.tv answer: HD before normal, German before English before
        /// language-neutral, then by likes.
        /// </summary>
        /// <param name="json">Answer of the fanart.tv v3 API.</param>
        /// <returns>The image URL, or null.</returns>
        public static string? PickFanartLogo(string json)
        {
            using var document = JsonDocument.Parse(json);
            var candidates = new List<(int Quality, int Language, int Likes, string Url)>();
            var fields = new[] { ("hdmovielogo", 0), ("hdtvlogo", 0), ("movielogo", 1), ("clearlogo", 1) };
            foreach (var (field, quality) in fields)
            {
                if (!document.RootElement.TryGetProperty(field, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in array.EnumerateArray())
                {
                    var url = entry.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var lang = entry.TryGetProperty("lang", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                    var language = lang switch { "de" => 0, "en" => 1, "00" or "" => 2, _ => 3 };
                    var likes = entry.TryGetProperty("likes", out var k) && int.TryParse(k.ValueKind == JsonValueKind.String ? k.GetString() : k.GetRawText(), out var n) ? n : 0;
                    candidates.Add((quality, language, likes, url));
                }
            }

            return candidates
                .OrderBy(c => c.Language)
                .ThenBy(c => c.Quality)
                .ThenByDescending(c => c.Likes)
                .Select(c => c.Url)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets the first available logo of a world from its sources (memory, disk cache or source).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="sources">Resolved sources in order of preference (see <see cref="ResolveAll"/>).</param>
        /// <param name="fanartKey">fanart.tv API key, if any.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The logo, or null.</returns>
        public async Task<Logo?> GetFirstAsync(string worldId, IReadOnlyList<string> sources, string? fanartKey, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sources);
            foreach (var source in sources)
            {
                var logo = await GetAsync(worldId, source, cancellationToken, fanartKey).ConfigureAwait(false);
                if (logo != null)
                {
                    return logo;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the logo of a world (from memory, disk cache or source).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="source">Resolved source (see <see cref="Resolve"/>).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="fanartKey">fanart.tv API key for "fanart:" sources.</param>
        /// <returns>The logo, or null.</returns>
        public async Task<Logo?> GetAsync(string worldId, string source, CancellationToken cancellationToken, string? fanartKey = null)
        {
            ArgumentNullException.ThrowIfNull(worldId);
            ArgumentNullException.ThrowIfNull(source);

            // Je Welt und Quelle getrennt zwischenspeichern
            var key = worldId + "-" + StableHash(source);
            if (_memory.TryGetValue(key, out var cached) && cached.Source == source && DateTime.UtcNow - cached.Fetched < RefreshAfter)
            {
                return cached;
            }

            if (cached == null && _failed.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < RetryFailedAfter && LoadFromDisk(key, source) == null)
            {
                return null;
            }

            await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stale = cached?.Source == source ? cached : LoadFromDisk(key, source);
                if (stale != null && DateTime.UtcNow - stale.Fetched < RefreshAfter)
                {
                    _memory[key] = stale;
                    return stale;
                }

                var fresh = await FetchAsync(source, fanartKey, cancellationToken).ConfigureAwait(false);
                if (fresh == null)
                {
                    // Quelle nicht erreichbar: alten Stand weiter ausliefern, sonst eine Stunde nicht erneut versuchen
                    _failed[key] = DateTime.UtcNow;
                    if (stale != null)
                    {
                        _memory[key] = stale;
                    }

                    return stale;
                }

                _failed.TryRemove(key, out _);
                _memory[key] = fresh;
                SaveToDisk(key, fresh);
                return fresh;
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".gif" => "image/gif",
            _ => "image/png"
        };

        private static string StableHash(string text)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        private async Task<Logo?> FetchAsync(string source, string? fanartKey, CancellationToken cancellationToken)
        {
            try
            {
                var match = FanartPattern.Match(source);
                if (match.Success)
                {
                    if (string.IsNullOrWhiteSpace(fanartKey))
                    {
                        return null;
                    }

                    var kind = match.Groups[1].Value.ToLowerInvariant() == "tv" ? "tv" : "movies";
                    var api = "https://webservice.fanart.tv/v3/" + kind + "/" + match.Groups[2].Value + "?api_key=" + Uri.EscapeDataString(fanartKey.Trim());
                    var jsonClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                    jsonClient.Timeout = TimeSpan.FromSeconds(20);
                    using var answer = await jsonClient.GetAsync(new Uri(api), cancellationToken).ConfigureAwait(false);
                    if (!answer.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("fanart.tv: kein Logo für {Source} (HTTP {Status})", source, (int)answer.StatusCode);
                        return null;
                    }

                    var url = PickFanartLogo(await answer.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (url == null)
                    {
                        _logger.LogInformation("fanart.tv: kein Logo für {Source}", source);
                        return null;
                    }

                    var image = await FetchAsync(url, null, cancellationToken).ConfigureAwait(false);
                    return image == null ? null : image with { Source = source };
                }

                if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(source);
                    if (!info.Exists || info.Length > MaxBytes)
                    {
                        return null;
                    }

                    return new Logo(source, await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false), ContentTypeFor(source), DateTime.UtcNow);
                }

                var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                using var response = await client.GetAsync(new Uri(source), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var contentType = response.Content.Headers.ContentType?.MediaType ?? ContentTypeFor(source);
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || response.Content.Headers.ContentLength > MaxBytes)
                {
                    _logger.LogWarning("Logo abgelehnt (kein Bild oder zu groß): {Source}", source);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (memory.Length > MaxBytes)
                    {
                        _logger.LogWarning("Logo abgelehnt (zu groß): {Source}", source);
                        return null;
                    }
                }

                return new Logo(source, memory.ToArray(), contentType, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is UnauthorizedAccessException || ex is UriFormatException || ex is JsonException)
            {
                _logger.LogWarning("Logo konnte nicht geladen werden ({Source}): {Message}", source, ex.Message);
                return null;
            }
        }

        private Logo? LoadFromDisk(string worldId, string source)
        {
            try
            {
                var metaPath = Path.Combine(_cacheDirectory, worldId + ".json");
                var dataPath = Path.Combine(_cacheDirectory, worldId + ".bin");
                if (!File.Exists(metaPath) || !File.Exists(dataPath))
                {
                    return null;
                }

                var meta = JsonSerializer.Deserialize<LogoMeta>(File.ReadAllText(metaPath));
                if (meta == null || meta.Source != source || string.IsNullOrEmpty(meta.ContentType))
                {
                    return null;
                }

                return new Logo(source, File.ReadAllBytes(dataPath), meta.ContentType, meta.Fetched);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void SaveToDisk(string worldId, Logo logo)
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                File.WriteAllBytes(Path.Combine(_cacheDirectory, worldId + ".bin"), logo.Data);
                File.WriteAllText(Path.Combine(_cacheDirectory, worldId + ".json"), JsonSerializer.Serialize(new LogoMeta { Source = logo.Source, ContentType = logo.ContentType, Fetched = logo.Fetched }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Logo konnte nicht zwischengespeichert werden");
            }
        }

        /// <summary>
        /// A logo image.
        /// </summary>
        /// <param name="Source">Source URL or file.</param>
        /// <param name="Data">Image bytes.</param>
        /// <param name="ContentType">MIME type.</param>
        /// <param name="Fetched">Time it was fetched (UTC).</param>
        public sealed record Logo(string Source, byte[] Data, string ContentType, DateTime Fetched);

        private sealed class LogoMeta
        {
            public string? Source { get; set; }

            public string? ContentType { get; set; }

            public DateTime Fetched { get; set; }
        }
    }
}
