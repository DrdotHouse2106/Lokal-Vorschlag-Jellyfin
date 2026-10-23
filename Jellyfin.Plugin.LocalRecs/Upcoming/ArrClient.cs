using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// Liest die Kalender von Sonarr und Radarr (<c>/api/v3/calendar</c>) und die Bilder vorgemerkter Filme.
    /// Der API-Schlüssel geht nur im Kopf <c>X-Api-Key</c> mit, nie in der Adresse – so landet er in keinem Log.
    /// </summary>
    public class ArrClient
    {
        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrClient"/> class.
        /// </summary>
        /// <param name="send">Sends a request (for tests), optional.</param>
        public ArrClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
        {
            _send = send ?? ((request, ct) => SharedClient.SendAsync(request, ct));
        }

        /// <summary>
        /// Gets the base address of Sonarr or Radarr without trailing slash, or null if it is not a valid http(s) address.
        /// </summary>
        /// <param name="url">Configured address.</param>
        /// <returns>The base address, or null.</returns>
        public static string? BaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        /// <summary>
        /// Reads Sonarr's calendar: episodes not yet downloaded with their series.
        /// </summary>
        /// <param name="json">Answer of <c>/api/v3/calendar?includeSeries=true</c>.</param>
        /// <returns>The episodes.</returns>
        public static List<ArrEpisode> ParseSonarrCalendar(string json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new List<ArrEpisode>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var e in document.RootElement.EnumerateArray())
            {
                if (Bool(e, "hasFile") || !TryDate(e, "airDateUtc", out var air) || !e.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(new ArrEpisode
                {
                    SonarrSeriesId = Int(e, "seriesId") ?? Int(series, "id") ?? 0,
                    SeriesTitle = Text(series, "title") ?? string.Empty,
                    TvdbId = Int(series, "tvdbId") is int tvdb && tvdb > 0 ? tvdb.ToString(CultureInfo.InvariantCulture) : null,
                    TmdbId = Int(series, "tmdbId") is int tmdb && tmdb > 0 ? tmdb.ToString(CultureInfo.InvariantCulture) : null,
                    ImdbId = Text(series, "imdbId"),
                    SeasonNumber = Int(e, "seasonNumber") ?? 0,
                    EpisodeNumber = Int(e, "episodeNumber") ?? 0,
                    Title = Text(e, "title"),
                    Overview = Text(e, "overview"),
                    Network = Text(series, "network"),
                    AirDateUtc = air
                });
            }

            return result;
        }

        /// <summary>
        /// Reads Radarr's calendar: monitored movies not yet downloaded with the digital or disc release.
        /// </summary>
        /// <param name="json">Answer of <c>/api/v3/calendar</c>.</param>
        /// <returns>The movies.</returns>
        public static List<ArrMovie> ParseRadarrCalendar(string json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new List<ArrMovie>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var m in document.RootElement.EnumerateArray())
            {
                if (Bool(m, "hasFile") || (m.TryGetProperty("monitored", out var monitored) && monitored.ValueKind == JsonValueKind.False))
                {
                    continue;
                }

                var images = m.TryGetProperty("images", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(i => Text(i, "coverType")).OfType<string>().ToList()
                    : new List<string>();
                result.Add(new ArrMovie
                {
                    RadarrId = Int(m, "id") ?? 0,
                    Title = Text(m, "title") ?? string.Empty,
                    Year = Int(m, "year"),
                    Overview = Text(m, "overview"),
                    TmdbId = Int(m, "tmdbId") is int tmdb && tmdb > 0 ? tmdb.ToString(CultureInfo.InvariantCulture) : null,
                    ImdbId = Text(m, "imdbId"),
                    Certification = Text(m, "certification"),
                    RootFolderPath = Text(m, "rootFolderPath") ?? Text(m, "path"),
                    InCinemas = TryDate(m, "inCinemas", out var cinema) ? cinema : null,
                    DigitalRelease = TryDate(m, "digitalRelease", out var digital) ? digital : null,
                    PhysicalRelease = TryDate(m, "physicalRelease", out var physical) ? physical : null,
                    HasPoster = images.Contains("poster", StringComparer.OrdinalIgnoreCase),
                    HasFanart = images.Contains("fanart", StringComparer.OrdinalIgnoreCase)
                });
            }

            return result.Where(m => m.RadarrId > 0).ToList();
        }

        /// <summary>
        /// Reads Sonarr's series list (<c>/api/v3/series</c>): ID, title, TVDB/TMDB/IMDb ID and network.
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>The series as episodes without episode data (series fields only).</returns>
        public static List<ArrEpisode> ParseSonarrSeries(string json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new List<ArrEpisode>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var series in document.RootElement.EnumerateArray())
            {
                if (Int(series, "id") is not int id)
                {
                    continue;
                }

                result.Add(new ArrEpisode
                {
                    SonarrSeriesId = id,
                    SeriesTitle = Text(series, "title") ?? string.Empty,
                    TvdbId = Int(series, "tvdbId") is int tvdb && tvdb > 0 ? tvdb.ToString(CultureInfo.InvariantCulture) : null,
                    TmdbId = Int(series, "tmdbId") is int tmdb && tmdb > 0 ? tmdb.ToString(CultureInfo.InvariantCulture) : null,
                    ImdbId = Text(series, "imdbId"),
                    Network = Text(series, "network")
                });
            }

            return result;
        }

        /// <summary>
        /// Reads all episodes of a series (<c>/api/v3/episode?seriesId=</c>), including downloaded ones (<see cref="ArrEpisode.HasFile"/>).
        /// Episodes without air date are left out.
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>The episodes.</returns>
        public static List<ArrEpisode> ParseSonarrEpisodes(string json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new List<ArrEpisode>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var e in document.RootElement.EnumerateArray())
            {
                if (!TryDate(e, "airDateUtc", out var air))
                {
                    continue;
                }

                result.Add(new ArrEpisode
                {
                    SonarrSeriesId = Int(e, "seriesId") ?? 0,
                    SeasonNumber = Int(e, "seasonNumber") ?? 0,
                    EpisodeNumber = Int(e, "episodeNumber") ?? 0,
                    Title = Text(e, "title"),
                    Overview = Text(e, "overview"),
                    AirDateUtc = air,
                    HasFile = Bool(e, "hasFile")
                });
            }

            return result;
        }

        /// <summary>
        /// Gets Sonarr's series list.
        /// </summary>
        /// <param name="baseUrl">Base address.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The series.</returns>
        public async Task<List<ArrEpisode>> GetSeriesListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            return ParseSonarrSeries(await GetStringAsync(baseUrl + "/api/v3/series", apiKey, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Gets all episodes of a Sonarr series.
        /// </summary>
        /// <param name="baseUrl">Base address.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="sonarrSeriesId">Sonarr's series ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The episodes.</returns>
        public async Task<List<ArrEpisode>> GetSeriesEpisodesAsync(string baseUrl, string apiKey, int sonarrSeriesId, CancellationToken cancellationToken)
        {
            return ParseSonarrEpisodes(await GetStringAsync(baseUrl + "/api/v3/episode?seriesId=" + sonarrSeriesId.ToString(CultureInfo.InvariantCulture), apiKey, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Gets Sonarr's calendar.
        /// </summary>
        /// <param name="baseUrl">Base address.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="startUtc">Start.</param>
        /// <param name="endUtc">End.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The episodes.</returns>
        public async Task<List<ArrEpisode>> GetEpisodesAsync(string baseUrl, string apiKey, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
        {
            var json = await GetStringAsync(baseUrl + "/api/v3/calendar?includeSeries=true&unmonitored=false&start=" + Iso(startUtc) + "&end=" + Iso(endUtc), apiKey, cancellationToken).ConfigureAwait(false);
            return ParseSonarrCalendar(json);
        }

        /// <summary>
        /// Gets Radarr's calendar.
        /// </summary>
        /// <param name="baseUrl">Base address.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="startUtc">Start.</param>
        /// <param name="endUtc">End.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The movies.</returns>
        public async Task<List<ArrMovie>> GetMoviesAsync(string baseUrl, string apiKey, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
        {
            var json = await GetStringAsync(baseUrl + "/api/v3/calendar?unmonitored=false&start=" + Iso(startUtc) + "&end=" + Iso(endUtc), apiKey, cancellationToken).ConfigureAwait(false);
            return ParseRadarrCalendar(json);
        }

        /// <summary>
        /// Gets a poster or fanart of a movie from Radarr.
        /// </summary>
        /// <param name="baseUrl">Base address.</param>
        /// <param name="apiKey">API key.</param>
        /// <param name="radarrId">Radarr's movie ID.</param>
        /// <param name="fanart">True for fanart, false for poster.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Image bytes and content type, or null.</returns>
        public async Task<(byte[] Data, string ContentType)?> GetImageAsync(string baseUrl, string apiKey, int radarrId, bool fanart, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/v3/mediacover/" + radarrId.ToString(CultureInfo.InvariantCulture) + (fanart ? "/fanart.jpg" : "/poster.jpg"));
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await _send(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return (await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), type);
        }

        private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

        private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

        private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static bool TryDate(JsonElement e, string name, out DateTime utc)
        {
            utc = default;
            if (Text(e, name) is not string text
                || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return false;
            }

            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        private async Task<string> GetStringAsync(string url, string apiKey, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _send(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Nur der Statuscode – weder Adresse noch Schlüssel
                throw new HttpRequestException("HTTP " + (int)response.StatusCode, null, response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
