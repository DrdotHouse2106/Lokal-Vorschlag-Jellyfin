using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Lädt den Profilbild-Katalog (Standard: Jellyfin-Welten) und die Icons, hält beides zwischengespeichert
    /// und merkt sich die Auswahl pro Benutzer (avatars/choices.json).
    /// </summary>
    public class AvatarService
    {
        /// <summary>Default catalog in the repository Jellyfin-Welten.</summary>
        public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Welten/main/icons/icons.json";

        /// <summary>Maximum size of an icon image.</summary>
        public const int MaxImageBytes = 2 * 1024 * 1024;

        private const int MaxCatalogBytes = 1024 * 1024;
        private static readonly TimeSpan CatalogReload = TimeSpan.FromHours(6);
        private static readonly TimeSpan CatalogRetry = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ImageReload = TimeSpan.FromDays(7);

        private readonly ILogger<AvatarService> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly string _directory;
        private readonly SemaphoreSlim _catalogLock = new SemaphoreSlim(1, 1);
        private readonly object _choicesLock = new object();
        private readonly Dictionary<string, CatalogEntry> _catalogs = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        private Dictionary<string, AvatarChoice>? _choices;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvatarService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="directory">Folder for the caches and choices.</param>
        /// <param name="httpClientFactory">HTTP client factory, optional.</param>
        public AvatarService(ILogger<AvatarService> logger, string directory, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>Gets the error of the last catalog load, if any.</summary>
        public string? LastError { get; private set; }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the configured catalog URL (empty setting = default catalog).
        /// </summary>
        /// <returns>URL or local path of icons.json.</returns>
        public static string ConfiguredCatalog()
        {
            var source = Config.AvatarCatalogUrl?.Trim();
            return string.IsNullOrEmpty(source) ? DefaultCatalogUrl : source;
        }

        /// <summary>
        /// Gets all configured catalogs: the main catalog (palette) and the additional ones (one per line).
        /// </summary>
        /// <returns>Sources without duplicates.</returns>
        public static IReadOnlyList<string> ConfiguredCatalogs()
        {
            var sources = new List<string> { ConfiguredCatalog() };
            foreach (var line in (Config.AvatarExtraCatalogUrls ?? string.Empty).Split('\n', '\r', ';'))
            {
                var source = line.Trim();
                if (source.Length > 0 && !sources.Contains(source, StringComparer.Ordinal))
                {
                    sources.Add(source);
                }
            }

            return sources;
        }

        /// <summary>
        /// Merges catalogs: palette, name and license of the first, categories of all (first ID wins).
        /// </summary>
        /// <param name="catalogs">Catalogs in order; null entries are skipped.</param>
        /// <returns>The merged catalog, or null if there is none.</returns>
        public static AvatarCatalog? Merge(IEnumerable<AvatarCatalog?> catalogs)
        {
            AvatarCatalog? merged = null;
            var licenses = new List<string>();
            foreach (var catalog in catalogs)
            {
                if (catalog == null)
                {
                    continue;
                }

                if (merged == null)
                {
                    merged = new AvatarCatalog { Name = catalog.Name };
                    merged.Colors.AddRange(catalog.Colors);
                }

                if (!string.IsNullOrEmpty(catalog.License) && !licenses.Contains(catalog.License))
                {
                    licenses.Add(catalog.License);
                }

                merged.Categories.AddRange(catalog.Categories.Where(c => merged.Categories.All(m => m.Id != c.Id)));
            }

            if (merged != null)
            {
                merged.License = string.Join(" · ", licenses);
            }

            return merged;
        }

        /// <summary>
        /// Gets the merged catalog of all configured sources; each is reloaded every 6 hours and falls back to its last good copy.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The catalog, or null if none could be loaded yet.</returns>
        public async Task<AvatarCatalog?> GetCatalogAsync(CancellationToken cancellationToken)
        {
            var sources = ConfiguredCatalogs();
            var loaded = new List<AvatarCatalog?>();
            var errors = new List<string>();
            foreach (var source in sources)
            {
                loaded.Add(await GetSingleAsync(source, errors, cancellationToken).ConfigureAwait(false));
            }

            LastError = errors.Count > 0 ? string.Join("; ", errors) : null;
            return Merge(loaded);
        }

        /// <summary>
        /// Gets the image of an icon (cached on disk for 7 days; older copies are used if the source is unreachable).
        /// Only PNG, JPEG and WebP are accepted.
        /// </summary>
        /// <param name="icon">Icon.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The image, or null.</returns>
        public async Task<AvatarImage?> GetImageAsync(AvatarIcon icon, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(icon);
            var path = Path.Combine(_directory, "icons", Hash(icon.Source) + ".bin");
            byte[]? cached = null;
            try
            {
                if (File.Exists(path))
                {
                    cached = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < ImageReload && AvatarRenderer.DetectContentType(cached) is string type)
                    {
                        return new AvatarImage(cached, type);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Icon-Zwischenspeicher nicht lesbar");
            }

            try
            {
                var data = await ReadAsync(icon.Source, MaxImageBytes, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                var contentType = AvatarRenderer.DetectContentType(data);
                if (contentType == null)
                {
                    _logger.LogWarning("Icon abgelehnt (kein PNG, JPEG oder WebP): {Source}", icon.Source);
                    return null;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Icon konnte nicht zwischengespeichert werden");
                }

                return new AvatarImage(data, contentType);
            }
            catch (Exception ex) when (IsLoadError(ex))
            {
                _logger.LogWarning("Icon konnte nicht geladen werden ({Source}): {Message}", icon.Source, ex.Message);
                return cached != null && AvatarRenderer.DetectContentType(cached) is string type ? new AvatarImage(cached, type) : null;
            }
        }

        /// <summary>
        /// Gets the last choice of a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <returns>The choice, or null.</returns>
        public AvatarChoice? GetChoice(Guid userId)
        {
            lock (_choicesLock)
            {
                return Choices().TryGetValue(userId.ToString("N"), out var choice)
                    ? new AvatarChoice { Icon = choice.Icon, Color = choice.Color, Updated = choice.Updated }
                    : null;
            }
        }

        /// <summary>
        /// Stores the choice of a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="icon">Icon key.</param>
        /// <param name="color">Color.</param>
        public void SetChoice(Guid userId, string icon, string color)
        {
            lock (_choicesLock)
            {
                Choices()[userId.ToString("N")] = new AvatarChoice { Icon = icon, Color = color, Updated = DateTime.UtcNow };
                try
                {
                    Directory.CreateDirectory(_directory);
                    var path = Path.Combine(_directory, "choices.json");
                    var temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(_choices));
                    File.Move(temp, path, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Profilbild-Auswahl konnte nicht gespeichert werden");
                }
            }
        }

        private static bool IsLoadError(Exception ex) =>
            ex is IOException || ex is JsonException || ex is HttpRequestException || ex is TaskCanceledException
            || ex is UnauthorizedAccessException || ex is ArgumentException || ex is UriFormatException || ex is NotSupportedException
            || ex is InvalidDataException;

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private async Task<AvatarCatalog?> GetSingleAsync(string source, List<string> errors, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            await _catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_catalogs.TryGetValue(source, out var entry))
                {
                    entry = new CatalogEntry { Catalog = LoadCatalogCache(source) };
                    _catalogs[source] = entry;
                }

                var due = entry.Catalog == null || entry.FromCache ? now - entry.Attempt >= CatalogRetry : now - entry.Loaded >= CatalogReload;
                if (due || (entry.Catalog == null && entry.Attempt == DateTime.MinValue))
                {
                    entry.Attempt = now;
                    try
                    {
                        var json = Encoding.UTF8.GetString(await ReadAsync(source, MaxCatalogBytes, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false));
                        entry.Catalog = AvatarCatalog.Parse(json, source);
                        entry.Loaded = now;
                        entry.FromCache = false;
                        entry.Error = null;
                        SaveCatalogCache(source, json);
                    }
                    catch (Exception ex) when (IsLoadError(ex))
                    {
                        entry.Error = ex.Message;
                        _logger.LogWarning("Profilbild-Katalog konnte nicht geladen werden ({Source}): {Message}", source, ex.Message);
                    }
                }

                if (entry.Error != null)
                {
                    errors.Add(source + ": " + entry.Error);
                }

                return entry.Catalog;
            }
            finally
            {
                _catalogLock.Release();
            }
        }

        private Dictionary<string, AvatarChoice> Choices()
        {
            if (_choices != null)
            {
                return _choices;
            }

            _choices = new Dictionary<string, AvatarChoice>();
            try
            {
                var path = Path.Combine(_directory, "choices.json");
                if (File.Exists(path))
                {
                    _choices = JsonSerializer.Deserialize<Dictionary<string, AvatarChoice>>(File.ReadAllText(path)) ?? _choices;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Profilbild-Auswahl konnte nicht gelesen werden");
            }

            return _choices;
        }

        private async Task<byte[]> ReadAsync(string source, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (!AvatarCatalog.IsUrl(source))
            {
                var info = new FileInfo(source);
                if (!info.Exists || info.Length > maxBytes)
                {
                    throw new InvalidDataException("Datei fehlt oder ist zu groß: " + source);
                }

                return await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
            }

            var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
            client.Timeout = timeout;
            using var response = await client.GetAsync(new Uri(source), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new InvalidDataException("Datei zu groß: " + source);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > maxBytes)
                {
                    throw new InvalidDataException("Datei zu groß: " + source);
                }
            }

            return memory.ToArray();
        }

        private string CatalogCachePath(string source) => Path.Combine(_directory, "catalog-" + Hash(source)[..16] + ".json");

        private AvatarCatalog? LoadCatalogCache(string source)
        {
            try
            {
                var path = CatalogCachePath(source);
                if (!File.Exists(path))
                {
                    return null;
                }

                var cache = JsonSerializer.Deserialize<CatalogCache>(File.ReadAllText(path));
                return cache?.Json != null && cache.Source == source ? AvatarCatalog.Parse(cache.Json, source) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Zwischengespeicherter Profilbild-Katalog nicht lesbar");
                return null;
            }
        }

        private void SaveCatalogCache(string source, string json)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(CatalogCachePath(source), JsonSerializer.Serialize(new CatalogCache { Source = source, Json = json }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Profilbild-Katalog konnte nicht zwischengespeichert werden");
            }
        }

        private sealed class CatalogEntry
        {
            public AvatarCatalog? Catalog { get; set; }

            public DateTime Loaded { get; set; } = DateTime.MinValue;

            public DateTime Attempt { get; set; } = DateTime.MinValue;

            public bool FromCache { get; set; } = true;

            public string? Error { get; set; }
        }

        private sealed class CatalogCache
        {
            public string? Source { get; set; }

            public string? Json { get; set; }
        }
    }
}
