using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.LocalRecs.Discover
{
    /// <summary>
    /// Deutsche Trailer von KinoCheck (api.kinocheck.de) als Ersatz, wenn TMDB nur fremdsprachige Videos hat. Gefragt wird
    /// nur mit der TMDB-ID eines Titels der Entdecken-Reihen – ohne Benutzer, ohne Bibliotheksdaten.
    /// </summary>
    public static class KinoCheck
    {
        /// <summary>Base address of the API.</summary>
        public const string ApiBase = "https://api.kinocheck.de/";

        /// <summary>
        /// Gets the request path for a title: movies <c>movies?tmdb_id=…&amp;language=de</c>, series <c>shows?…</c>.
        /// </summary>
        /// <param name="tmdbId">The TMDB ID.</param>
        /// <param name="series">True for series.</param>
        /// <returns>The path.</returns>
        public static string Path(int tmdbId, bool series) =>
            (series ? "shows" : "movies") + "?tmdb_id=" + tmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&language=de";

        /// <summary>
        /// Picks the best German trailer of an answer: the field <c>trailer</c> and the list <c>videos</c>, each with
        /// <c>youtube_video_id</c>, <c>title</c>, <c>language</c> and <c>categories</c>. Trailer before teaser, other
        /// categories (clips, reviews) are skipped, as are foreign videos; German dubbed before German subtitles (title with „OmU“). An error object
        /// (KinoCheck answers „not found“ with status 200 and <c>error</c>) yields null.
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>YouTube key, title and language mark („de“ or „OmU“), or null.</returns>
        public static (string Key, string? Name, string Language)? PickTrailer(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var candidates = new List<(string Key, string? Name, string Language, int Rank, int Order)>();
                void Add(JsonElement video, int order)
                {
                    if (video.ValueKind != JsonValueKind.Object || Text(video, "youtube_video_id") is not string key)
                    {
                        return;
                    }

                    var categories = video.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
                        ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToList()
                        : new List<string>();
                    var name = Text(video, "title");

                    // Nur Trailer und Teaser (keine Clips, Kritiken o. Ä.); ohne Kategorien entscheidet der Titel
                    var title = categories.Count == 0 ? name ?? string.Empty : string.Empty;
                    var rank = categories.Any(x => x.Equals("Trailer", StringComparison.OrdinalIgnoreCase)) || title.Contains("trailer", StringComparison.OrdinalIgnoreCase) ? 0
                        : categories.Any(x => x.Equals("Teaser", StringComparison.OrdinalIgnoreCase)) || title.Contains("teaser", StringComparison.OrdinalIgnoreCase) ? 1 : -1;

                    // Sprache auch am Titel: „German Deutsch OmU“ ist untertitelt, obwohl KinoCheck „de“ meldet
                    var language = TmdbDiscover.TrailerLanguage(name, Text(video, "language") ?? "de");
                    if (rank >= 0 && (language == "de" || language == "OmU"))
                    {
                        candidates.Add((key, name, language, rank, order));
                    }
                }

                if (root.TryGetProperty("trailer", out var trailer))
                {
                    Add(trailer, 0);
                }

                if (root.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Array)
                {
                    var order = 1;
                    foreach (var video in videos.EnumerateArray())
                    {
                        Add(video, order++);
                    }
                }

                // Synchronisiert vor OmU, dann Trailer vor Teaser
                var best = candidates.OrderBy(x => TmdbDiscover.LanguageRank(x.Language)).ThenBy(x => x.Rank).ThenBy(x => x.Order).FirstOrDefault();
                return best.Key == null ? null : (best.Key, best.Name, best.Language);
            }
        }

        private static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
    }
}
