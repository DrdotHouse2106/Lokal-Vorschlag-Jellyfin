using System;
using System.Collections.Concurrent;
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
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg", ".gif" };

        private readonly ILogger<WorldLogoService> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly string _cacheDirectory;
        private readonly ConcurrentDictionary<string, Logo> _memory = new ConcurrentDictionary<string, Logo>(StringComparer.Ordinal);
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
        /// <returns>The resolved source, or null if it is not allowed.</returns>
        public static string? Resolve(string? logo, string? listSource)
        {
            if (string.IsNullOrWhiteSpace(logo))
            {
                return null;
            }

            logo = logo.Trim();
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
        /// Gets the logo of a world (from memory, disk cache or source).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="source">Resolved source (see <see cref="Resolve"/>).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The logo, or null.</returns>
        public async Task<Logo?> GetAsync(string worldId, string source, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(worldId);
            ArgumentNullException.ThrowIfNull(source);
            if (_memory.TryGetValue(worldId, out var cached) && cached.Source == source && DateTime.UtcNow - cached.Fetched < RefreshAfter)
            {
                return cached;
            }

            await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stale = cached?.Source == source ? cached : LoadFromDisk(worldId, source);
                if (stale != null && DateTime.UtcNow - stale.Fetched < RefreshAfter)
                {
                    _memory[worldId] = stale;
                    return stale;
                }

                var fresh = await FetchAsync(source, cancellationToken).ConfigureAwait(false);
                if (fresh == null)
                {
                    // Quelle nicht erreichbar: alten Stand weiter ausliefern
                    if (stale != null)
                    {
                        _memory[worldId] = stale;
                    }

                    return stale;
                }

                _memory[worldId] = fresh;
                SaveToDisk(worldId, fresh);
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

        private async Task<Logo?> FetchAsync(string source, CancellationToken cancellationToken)
        {
            try
            {
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
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is UnauthorizedAccessException || ex is UriFormatException)
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
