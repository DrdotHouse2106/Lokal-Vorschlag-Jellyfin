using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Eigener Fernsehprogramm-Anbieter („LocalRecs EPG“) für XMLTV-Quellen. Ersetzt den eingebauten XMLTV-Anbieter:
    /// Film oder Serie entscheiden Regel-Bausteine statt einer Kategorienliste, Staffel/Folge/Folgentitel bleiben erhalten.
    /// </summary>
    public class EpgListingsProvider : IListingsProvider
    {
        /// <summary>Provider type stored in the live TV configuration.</summary>
        public const string ProviderType = "localrecs-epg";

        private static readonly TimeSpan MaxCacheAge = TimeSpan.FromHours(1);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

        private readonly IApplicationPaths _paths;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly XmltvSource? _extraSource;
        private readonly ILogger<EpgListingsProvider> _logger;
        private readonly ConcurrentDictionary<string, DateTime> _failures = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, (DateTime Written, EpgFile File)> _parsed = new Dictionary<string, (DateTime, EpgFile)>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="EpgListingsProvider"/> class.
        /// </summary>
        /// <param name="paths">Application paths (cache folder).</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="httpClientFactory">HTTP client factory, optional.</param>
        /// <param name="extraSource">The „XMLTV-Quelle für Darsteller“ for movie hints (director, IMDb, year), optional.</param>
        public EpgListingsProvider(IApplicationPaths paths, ILogger<EpgListingsProvider> logger, IHttpClientFactory? httpClientFactory = null, XmltvSource? extraSource = null)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory;
            _extraSource = extraSource;
        }

        /// <inheritdoc />
        public string Name => "LocalRecs EPG";

        /// <inheritdoc />
        public string Type => ProviderType;

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Loads and parses the XMLTV file of a provider (download cached for an hour, parse cached until the file changes).
        /// </summary>
        /// <param name="info">Provider settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The file.</returns>
        public async Task<EpgFile> GetFileAsync(ListingsProviderInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            var path = await DownloadAsync(info, cancellationToken).ConfigureAwait(false);
            var written = File.GetLastWriteTimeUtc(path);
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_parsed.TryGetValue(path, out var cached) && cached.Written == written)
                {
                    return cached.File;
                }

                using var stream = File.OpenRead(path);
                var file = EpgFile.Read(stream, info.PreferredLanguage);
                _parsed[path] = (written, file);
                _logger.LogInformation("LocalRecs EPG: {Channels} Sender, {Programmes} Sendungen gelesen ({Path})", file.Channels.Count, file.Programmes.Count, info.Path);
                return file;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            var file = await GetFileAsync(info, cancellationToken).ConfigureAwait(false);
            var channelName = file.Channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.Ordinal))?.Name;
            var settings = EpgRuleSettings.From(Config);
            await WaitForExtraSourceAsync(cancellationToken).ConfigureAwait(false);
            if (!file.ByChannel.TryGetValue(channelId, out var programmes))
            {
                return Array.Empty<ProgramInfo>();
            }

            return programmes
                .Where(p => p.Stop.UtcDateTime > startDateUtc && p.Start.UtcDateTime < endDateUtc)
                .Select(p => EpgMapper.ToProgramInfo(p, EpgClassifier.Classify(p, channelName, settings, Extra(p, channelName))))
                .ToList();
        }

        /// <summary>
        /// Gets movie hints for a programme from the „XMLTV-Quelle für Darsteller“ – only if the programme itself has
        /// no director and no IMDb link (e.g. m3u-editor passes on a reduced XMLTV). Match: start ±15 min and title.
        /// </summary>
        /// <param name="programme">The programme.</param>
        /// <param name="channelName">Channel name (preferred when several match).</param>
        /// <returns>The hints, or null.</returns>
        public EpgExtra? Extra(EpgProgramme programme, string? channelName)
        {
            ArgumentNullException.ThrowIfNull(programme);
            if (_extraSource == null || programme.Directors.Count > 0 || programme.Urls.Count > 0)
            {
                return null;
            }

            var match = _extraSource.Lookup(programme.Start.UtcDateTime, programme.Title, programme.SubTitle, channelName);
            if (match == null || (match.Directors.Count == 0 && match.Year == null && string.IsNullOrEmpty(match.ImdbId)))
            {
                return null;
            }

            var extra = new EpgExtra { Year = match.Year, ImdbId = match.ImdbId };
            extra.Directors.AddRange(match.Directors);
            return extra;
        }

        /// <summary>
        /// Waits (at most 2 minutes) until the „XMLTV-Quelle für Darsteller“ is loaded, so the first guide refresh
        /// after a restart already has the movie hints.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task WaitForExtraSourceAsync(CancellationToken cancellationToken)
        {
            if (_extraSource == null || string.IsNullOrWhiteSpace(Config.LiveTvXmltvSource))
            {
                return;
            }

            _extraSource.EnsureFresh();
            for (var i = 0; i < 120 && _extraSource.LastLoaded == null && _extraSource.LastError == null; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (string.IsNullOrWhiteSpace(info.Path))
            {
                throw new ArgumentException("Bitte die Adresse oder den Pfad der XMLTV-Datei angeben.");
            }

            // Speichern ist ein ausdrücklicher neuer Versuch
            _failures.TryRemove(info.Path, out _);
            if (!IsUrl(info.Path) && !File.Exists(info.Path))
            {
                throw new FileNotFoundException("XMLTV-Datei nicht gefunden:", info.Path);
            }

            if (!string.IsNullOrEmpty(info.Id))
            {
                TryDelete(CachePath(info));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        {
            var file = await GetFileAsync(info, CancellationToken.None).ConfigureAwait(false);
            return file.Channels.Select(c => new NameIdPair { Id = c.Id, Name = c.Name }).ToList();
        }

        /// <inheritdoc />
        public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
        {
            var file = await GetFileAsync(info, cancellationToken).ConfigureAwait(false);
            return file.Channels.Select(c => new ChannelInfo
            {
                Id = c.Id,
                Name = c.Name,
                ImageUrl = string.IsNullOrEmpty(c.IconUrl) ? null : c.IconUrl,
                Number = string.IsNullOrWhiteSpace(c.Number) ? c.Id : c.Number
            }).ToList();
        }

        /// <summary>
        /// Drops the downloaded copies, so the next guide refresh loads the source again.
        /// </summary>
        public void ClearCache()
        {
            _failures.Clear();
            try
            {
                var directory = Path.Combine(_paths.CachePath, "localrecs-epg");
                if (Directory.Exists(directory))
                {
                    foreach (var file in Directory.GetFiles(directory, "*.xml"))
                    {
                        TryDelete(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "EPG-Zwischenspeicher konnte nicht geleert werden");
            }
        }

        private static bool IsUrl(string path) => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static bool IsGzip(string path)
        {
            var clean = path.Split('?')[0];
            return clean.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || clean.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase);
        }

        private string CachePath(ListingsProviderInfo info) => Path.Combine(_paths.CachePath, "localrecs-epg", (string.IsNullOrEmpty(info.Id) ? "neu" : info.Id) + ".xml");

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Datei konnte nicht gelöscht werden: {Path}", path);
            }
        }

        private async Task<string> DownloadAsync(ListingsProviderInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(info.Path))
            {
                throw new InvalidOperationException("Keine XMLTV-Quelle eingetragen");
            }

            // Lokale, unkomprimierte Dateien direkt lesen
            if (!IsUrl(info.Path) && !IsGzip(info.Path))
            {
                return info.Path;
            }

            var cacheFile = CachePath(info);
            if (File.Exists(cacheFile) && File.GetLastWriteTimeUtc(cacheFile) >= DateTime.UtcNow - MaxCacheAge)
            {
                return cacheFile;
            }

            if (_failures.TryGetValue(info.Path, out var failed) && DateTime.UtcNow - failed < MaxCacheAge && File.Exists(cacheFile))
            {
                return cacheFile;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            var temp = cacheFile + ".tmp";
            try
            {
                using var timeout = new CancellationTokenSource(DownloadTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                Stream source;
                HttpResponseMessage? response = null;
                if (IsUrl(info.Path))
                {
                    var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                    client.Timeout = DownloadTimeout;
                    if (!string.IsNullOrWhiteSpace(info.UserAgent))
                    {
                        client.DefaultRequestHeaders.UserAgent.TryParseAdd(info.UserAgent);
                    }

                    response = await client.GetAsync(new Uri(info.Path), HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                }
                else
                {
                    source = File.OpenRead(info.Path);
                }

                using (response)
                await using (source.ConfigureAwait(false))
                {
                    var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                    await using (target.ConfigureAwait(false))
                    {
                        if (IsGzip(info.Path) || (response?.Content.Headers.ContentType?.MediaType?.Contains("gzip", StringComparison.OrdinalIgnoreCase) ?? false))
                        {
                            var unzip = new GZipStream(source, CompressionMode.Decompress);
                            await using (unzip.ConfigureAwait(false))
                            {
                                await unzip.CopyToAsync(target, linked.Token).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await source.CopyToAsync(target, linked.Token).ConfigureAwait(false);
                        }
                    }
                }

                if (new FileInfo(temp).Length == 0)
                {
                    throw new InvalidDataException("Leere XMLTV-Datei: " + info.Path);
                }

                File.Move(temp, cacheFile, true);
                _failures.TryRemove(info.Path, out _);
                return cacheFile;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is InvalidDataException || ex is UriFormatException)
            {
                TryDelete(temp);
                _failures[info.Path] = DateTime.UtcNow;
                _logger.LogWarning("LocalRecs EPG: XMLTV-Quelle nicht ladbar ({Path}): {Message}", info.Path, ex.Message);
                if (File.Exists(cacheFile))
                {
                    return cacheFile;
                }

                throw;
            }
        }
    }
}
