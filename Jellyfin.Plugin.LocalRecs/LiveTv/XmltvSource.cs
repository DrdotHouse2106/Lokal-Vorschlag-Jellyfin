using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Liest Darsteller, Regie, Produktionsjahr und Land direkt aus einer XMLTV-Datei. Jellyfin speichert für
    /// EPG-Sendungen keine Personen, und manche EPG-Werkzeuge entfernen sie beim Weitergeben. Die Datei wird im
    /// Hintergrund geladen und neu gelesen, wenn sie sich ändert (Pfad) bzw. alle 6 Stunden (URL).
    /// Die Zuordnung zu den Jellyfin-Sendungen erfolgt über Startzeit (UTC, auf die Minute) und Titel,
    /// weil sich die Senderkennungen oft unterscheiden.
    /// </summary>
    public class XmltvSource
    {
        private const int MaxActors = 10;
        private const int MaxDirectors = 3;
        private static readonly TimeSpan ReloadInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

        private readonly ILogger<XmltvSource> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly object _lock = new object();
        private Dictionary<string, List<XmltvProgramme>> _index = new Dictionary<string, List<XmltvProgramme>>();
        private string? _loadedSource;
        private DateTime _lastWriteTime;
        private DateTime _lastAttempt = DateTime.MinValue;
        private bool _loading;

        /// <summary>
        /// Initializes a new instance of the <see cref="XmltvSource"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="httpClientFactory">HTTP client factory (for URLs).</param>
        public XmltvSource(ILogger<XmltvSource> logger, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Gets the number of programmes read at the last load.
        /// </summary>
        public int ProgrammeCount { get; private set; }

        /// <summary>
        /// Gets the number of programmes with people at the last load.
        /// </summary>
        public int ProgrammesWithPeople { get; private set; }

        /// <summary>
        /// Gets the time of the last successful load (UTC).
        /// </summary>
        public DateTime? LastLoaded { get; private set; }

        /// <summary>
        /// Gets the error of the last load attempt, if any.
        /// </summary>
        public string? LastError { get; private set; }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Reads an XMLTV document (plain or gzip-compressed) into a lookup index.
        /// </summary>
        /// <param name="stream">The XMLTV data.</param>
        /// <returns>Programmes by key (see <see cref="Key"/>).</returns>
        public static Dictionary<string, List<XmltvProgramme>> Parse(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            var buffered = stream.CanSeek ? stream : new BufferedStream(stream);
            var input = IsGzip(buffered) ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            var index = new Dictionary<string, List<XmltvProgramme>>();
            using var reader = XmlReader.Create(input, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "programme")
                {
                    continue;
                }

                var start = ParseXmltvTime(reader.GetAttribute("start"));
                using var programmeReader = reader.ReadSubtree();
                var programme = ReadProgramme(programmeReader);
                if (start == null || string.IsNullOrWhiteSpace(programme.Title))
                {
                    continue;
                }

                var key = Key(start.Value, programme.Title);
                if (!index.TryGetValue(key, out var list))
                {
                    list = new List<XmltvProgramme>();
                    index[key] = list;
                }

                list.Add(programme);
            }

            return index;
        }

        /// <summary>
        /// Builds the lookup key from start time and title.
        /// </summary>
        /// <param name="startUtc">Start time (UTC).</param>
        /// <param name="title">Programme title.</param>
        /// <returns>The key.</returns>
        public static string Key(DateTime startUtc, string? title)
        {
            var utc = startUtc.Kind == DateTimeKind.Local ? startUtc.ToUniversalTime() : startUtc;
            return utc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + "|" + LiveTvText.NormalizeFeature(title);
        }

        /// <summary>
        /// Finds a programme in an index. With several programmes at the same time and title the episode title decides.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="startUtc">Start time (UTC).</param>
        /// <param name="title">Title.</param>
        /// <param name="episodeTitle">Episode title.</param>
        /// <returns>The programme, or null.</returns>
        public static XmltvProgramme? Find(IReadOnlyDictionary<string, List<XmltvProgramme>> index, DateTime startUtc, string? title, string? episodeTitle)
        {
            ArgumentNullException.ThrowIfNull(index);
            if (string.IsNullOrWhiteSpace(title) || !index.TryGetValue(Key(startUtc, title), out var list) || list.Count == 0)
            {
                return null;
            }

            if (list.Count == 1)
            {
                return list[0];
            }

            var episode = LiveTvText.NormalizeFeature(episodeTitle);
            return list.FirstOrDefault(p => LiveTvText.NormalizeFeature(p.EpisodeTitle) == episode) ?? list[0];
        }

        /// <summary>
        /// Looks up the XMLTV data of a programme. Starts a (re)load in the background if needed.
        /// </summary>
        /// <param name="startUtc">Start time (UTC).</param>
        /// <param name="title">Title.</param>
        /// <param name="episodeTitle">Episode title.</param>
        /// <returns>The programme, or null.</returns>
        public XmltvProgramme? Lookup(DateTime startUtc, string? title, string? episodeTitle)
        {
            EnsureFresh();
            Dictionary<string, List<XmltvProgramme>> index;
            lock (_lock)
            {
                index = _index;
            }

            return Find(index, startUtc, title, episodeTitle);
        }

        /// <summary>
        /// Starts a background (re)load if the configured source is new, changed or old.
        /// </summary>
        public void EnsureFresh()
        {
            var source = Config.LiveTvXmltvSource?.Trim();
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(source))
                {
                    if (_loadedSource != null)
                    {
                        _index = new Dictionary<string, List<XmltvProgramme>>();
                        _loadedSource = null;
                        ProgrammeCount = 0;
                        ProgrammesWithPeople = 0;
                        LastLoaded = null;
                    }

                    LastError = null;
                    return;
                }

                if (_loading || !NeedsReload(source, now))
                {
                    return;
                }

                _loading = true;
                _lastAttempt = now;
            }

            _ = Task.Run(() => LoadAsync(source));
        }

        private static bool IsGzip(Stream stream)
        {
            if (!stream.CanSeek)
            {
                return false;
            }

            var position = stream.Position;
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            stream.Position = position;
            return first == 0x1f && second == 0x8b;
        }

        private static bool IsUrl(string source) =>
            source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses an XMLTV time such as "20261016070500 +0000" to UTC.
        /// </summary>
        private static DateTime? ParseXmltvTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0].Length < 12
                || !DateTime.TryParseExact(parts[0].Substring(0, Math.Min(14, parts[0].Length)), parts[0].Length >= 14 ? "yyyyMMddHHmmss" : "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return null;
            }

            var offset = TimeSpan.Zero;
            if (parts.Length > 1 && parts[1].Length == 5 && (parts[1][0] == '+' || parts[1][0] == '-')
                && int.TryParse(parts[1].AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                && int.TryParse(parts[1].AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (parts[1][0] == '-')
                {
                    offset = -offset;
                }
            }

            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        private static XmltvProgramme ReadProgramme(XmlReader reader)
        {
            var programme = new XmltvProgramme();
            reader.Read();
            reader.Read();

            // ReadElementContentAsString steht danach schon auf dem nächsten Knoten – daher nur sonst weiterlesen
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "title":
                        var title = reader.ReadElementContentAsString().Trim();
                        programme.Title ??= title;
                        break;
                    case "sub-title":
                        var episode = reader.ReadElementContentAsString().Trim();
                        programme.EpisodeTitle ??= episode;
                        break;
                    case "actor":
                        var role = reader.GetAttribute("role")?.Trim();
                        var actor = reader.ReadElementContentAsString().Trim();
                        if (AddName(programme.Actors, actor, MaxActors))
                        {
                            programme.Roles.Add(string.IsNullOrEmpty(role) ? null : role);
                        }

                        break;
                    case "writer":
                        AddName(programme.Writers, reader.ReadElementContentAsString(), MaxDirectors);
                        break;
                    case "episode-num":
                        var system = reader.GetAttribute("system")?.Trim().ToLowerInvariant();
                        ReadEpisodeNum(programme, system, reader.ReadElementContentAsString().Trim());
                        break;
                    case "director":
                        AddName(programme.Directors, reader.ReadElementContentAsString(), MaxDirectors);
                        break;
                    case "date":
                        var date = reader.ReadElementContentAsString().Trim();
                        if (programme.Year == null && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year > 1880 && year < 2200)
                        {
                            programme.Year = year;
                        }

                        break;
                    case "country":
                        var country = reader.ReadElementContentAsString().Trim();
                        programme.Country ??= country;
                        break;
                    default:
                        reader.Read();
                        break;
                }
            }

            return programme;
        }

        private static bool AddName(List<string> names, string? name, int max)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || names.Count >= max || names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            names.Add(name);
            return true;
        }

        /// <summary>
        /// Reads an episode-num element: "onscreen" (e.g. "S11 E8/12") is preferred, "xmltv_ns" (zero-based
        /// "10.7/12.") is converted; imdb.com and themoviedb.org values are kept as external IDs.
        /// </summary>
        private static void ReadEpisodeNum(XmltvProgramme programme, string? system, string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            switch (system)
            {
                case "onscreen":
                    programme.Episode = value;
                    break;
                case "xmltv_ns" when programme.Episode == null:
                    var parts = value.Split('.');
                    var season = ParseXmltvNsPart(parts.ElementAtOrDefault(0));
                    var episode = ParseXmltvNsPart(parts.ElementAtOrDefault(1));
                    if (season != null || episode != null)
                    {
                        programme.Episode = string.Join(' ', new[] { season == null ? null : "S" + season, episode == null ? null : "E" + episode }.Where(p => p != null));
                    }

                    break;
                case "imdb.com":
                case "imdb":
                    var imdb = System.Text.RegularExpressions.Regex.Match(value, "tt\\d+");
                    if (imdb.Success)
                    {
                        programme.ImdbId ??= imdb.Value;
                    }

                    break;
                case "themoviedb.org":
                case "tmdb":
                    var tmdb = System.Text.RegularExpressions.Regex.Match(value, "\\d+");
                    if (tmdb.Success)
                    {
                        programme.TmdbId ??= tmdb.Value;
                    }

                    break;
            }
        }

        private static int? ParseXmltvNsPart(string? part)
        {
            var number = part?.Split('/')[0].Trim();
            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value + 1 : null;
        }

        private bool NeedsReload(string source, DateTime now)
        {
            if (!string.Equals(source, _loadedSource, StringComparison.Ordinal))
            {
                // Neue Quelle: sofort, nach einem Fehler aber nicht öfter als alle 15 Minuten
                return LastError == null || now - _lastAttempt >= RetryInterval;
            }

            if (IsUrl(source))
            {
                return now - _lastAttempt >= ReloadInterval;
            }

            if (now - _lastAttempt < TimeSpan.FromMinutes(1))
            {
                return false;
            }

            try
            {
                return File.GetLastWriteTimeUtc(source) != _lastWriteTime || now - _lastAttempt >= ReloadInterval;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return false;
            }
        }

        private async Task LoadAsync(string source)
        {
            try
            {
                Dictionary<string, List<XmltvProgramme>> index;
                var writeTime = DateTime.MinValue;
                if (IsUrl(source))
                {
                    var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                    client.Timeout = TimeSpan.FromMinutes(2);
                    using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    using var memory = new MemoryStream();
                    await response.Content.CopyToAsync(memory).ConfigureAwait(false);
                    memory.Position = 0;
                    index = Parse(memory);
                }
                else
                {
                    writeTime = File.GetLastWriteTimeUtc(source);
                    using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
                    index = Parse(file);
                }

                var count = index.Values.Sum(l => l.Count);
                var withPeople = index.Values.Sum(l => l.Count(p => p.People.Count > 0));
                lock (_lock)
                {
                    _index = index;
                    _loadedSource = source;
                    _lastWriteTime = writeTime;
                    ProgrammeCount = count;
                    ProgrammesWithPeople = withPeople;
                    LastLoaded = DateTime.UtcNow;
                    LastError = null;
                }

                _logger.LogInformation("XMLTV-Quelle geladen: {Count} Sendungen, davon {WithPeople} mit Personen ({Source})", count, withPeople, source);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is HttpRequestException
                || ex is InvalidDataException || ex is TaskCanceledException || ex is ArgumentException || ex is NotSupportedException)
            {
                lock (_lock)
                {
                    LastError = ex.Message;
                }

                _logger.LogWarning("XMLTV-Quelle konnte nicht gelesen werden ({Source}): {Message}", source, ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    _loading = false;
                }
            }
        }

        /// <summary>
        /// Data of one XMLTV programme.
        /// </summary>
        public class XmltvProgramme
        {
            /// <summary>Gets or sets the title.</summary>
            public string? Title { get; set; }

            /// <summary>Gets or sets the episode title (sub-title).</summary>
            public string? EpisodeTitle { get; set; }

            /// <summary>Gets the actors (max. 10).</summary>
            public List<string> Actors { get; } = new List<string>();

            /// <summary>Gets the directors (max. 3).</summary>
            public List<string> Directors { get; } = new List<string>();

            /// <summary>Gets or sets the production year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets the country.</summary>
            public string? Country { get; set; }

            /// <summary>Gets the roles of the actors (same order as <see cref="Actors"/>, null if unknown).</summary>
            public List<string?> Roles { get; } = new List<string?>();

            /// <summary>Gets the writers (max. 3).</summary>
            public List<string> Writers { get; } = new List<string>();

            /// <summary>Gets or sets the episode number as shown on screen (e.g. "S11 E8/12").</summary>
            public string? Episode { get; set; }

            /// <summary>Gets or sets the IMDb ID (e.g. "tt1234567").</summary>
            public string? ImdbId { get; set; }

            /// <summary>Gets or sets the TMDb ID.</summary>
            public string? TmdbId { get; set; }

            /// <summary>Gets actors and directors together.</summary>
            public IReadOnlyList<string> People => Actors.Concat(Directors).ToList();
        }
    }
}
