using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.LocalRecs.Upcoming;

namespace Jellyfin.Plugin.LocalRecs.Discover
{
    /// <summary>
    /// Liest die Antworten von TMDB für die Entdecken-Reihen: Listen (<c>movie/upcoming</c>, <c>discover/movie</c>,
    /// <c>discover/tv</c>) und Details mit <c>release_dates</c> bzw. <c>content_ratings</c>, <c>videos</c> und <c>watch/providers</c>.
    /// </summary>
    public static class TmdbDiscover
    {
        /// <summary>Base address of TMDB images.</summary>
        public const string ImageBase = "https://image.tmdb.org/t/p/";

        /// <summary>Most providers per title.</summary>
        public const int MaxProviders = 3;

        // Übliche deutsche Schreibweisen der Anbieter
        private static readonly Dictionary<string, string> ProviderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anime Digital Network"] = "ADN",
            ["Crunchyroll Premium"] = "Crunchyroll",
            ["Amazon Prime"] = "Amazon Prime Video",
            ["Max"] = "HBO Max"
        };

        /// <summary>
        /// Reads the IDs (with popularity) of a list answer.
        /// </summary>
        /// <param name="json">The answer.</param>
        /// <returns>IDs and popularity.</returns>
        public static List<(int Id, double Popularity)> ParseList(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new List<(int Id, double Popularity)>();
            }

            return results.EnumerateArray()
                .Where(r => r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                .Select(r => (r.GetProperty("id").GetInt32(), r.TryGetProperty("popularity", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0))
                .ToList();
        }

        /// <summary>
        /// Reads the details of a movie or series.
        /// </summary>
        /// <param name="json">Answer of <c>movie/{id}</c> or <c>tv/{id}</c> with appended data.</param>
        /// <param name="series">True for a series.</param>
        /// <param name="country">Country code, e.g. „DE“.</param>
        /// <returns>The item (dates not yet evaluated), with the country's release dates by type.</returns>
        public static (DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases)? ParseDetails(string json, bool series, string country)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var item = new DiscoverItem
            {
                TmdbId = idElement.GetInt32(),
                MediaType = series ? "tv" : "movie",
                Title = Text(root, series ? "name" : "title") ?? string.Empty,
                OriginalTitle = Text(root, series ? "original_name" : "original_title"),
                Overview = Text(root, "overview"),
                PosterPath = Text(root, "poster_path"),
                BackdropPath = Text(root, "backdrop_path"),
                Popularity = root.TryGetProperty("popularity", out var pop) && pop.ValueKind == JsonValueKind.Number ? pop.GetDouble() : 0,
                VoteCount = root.TryGetProperty("vote_count", out var votes) && votes.ValueKind == JsonValueKind.Number ? votes.GetInt32() : 0
            };
            item.PosterUrl = item.PosterPath == null ? null : ImageBase + "w342" + item.PosterPath;
            item.BackdropUrl = item.BackdropPath == null ? null : ImageBase + "w1280" + item.BackdropPath;
            if (root.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            {
                item.Genres = genres.EnumerateArray().Select(g => Text(g, "name")).OfType<string>().ToList();
            }

            if (series)
            {
                item.RuntimeMinutes = root.TryGetProperty("episode_run_time", out var runtimes) && runtimes.ValueKind == JsonValueKind.Array
                    ? runtimes.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Number).Select(r => (int?)r.GetInt32()).FirstOrDefault()
                    : null;
                item.OfficialRating = root.TryGetProperty("content_ratings", out var ratings) && ratings.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(r => string.Equals(Text(r, "iso_3166_1"), country, StringComparison.OrdinalIgnoreCase)).Select(r => Text(r, "rating")).FirstOrDefault(r => r != null)
                    : null;
            }
            else
            {
                item.RuntimeMinutes = root.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Number && runtime.GetInt32() > 0 ? runtime.GetInt32() : null;
            }

            var releases = new List<(int Type, DateOnly Date)>();
            if (!series && root.TryGetProperty("release_dates", out var releaseDates) && releaseDates.TryGetProperty("results", out var byCountry) && byCountry.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in byCountry.EnumerateArray().Where(r => string.Equals(Text(r, "iso_3166_1"), country, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!entry.TryGetProperty("release_dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var date in dates.EnumerateArray())
                    {
                        var type = date.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                        if (Text(date, "release_date") is string text && text.Length >= 10 && DateOnly.TryParse(text.AsSpan(0, 10), CultureInfo.InvariantCulture, out var day))
                        {
                            releases.Add((type, day));
                        }

                        item.OfficialRating ??= Text(date, "certification");
                    }
                }
            }

            var primary = Text(root, series ? "first_air_date" : "release_date");
            if (primary != null && primary.Length >= 10 && DateOnly.TryParse(primary.AsSpan(0, 10), CultureInfo.InvariantCulture, out var primaryDay))
            {
                releases.Add((0, primaryDay));
                item.Year = primaryDay.Year;
            }

            (item.TrailerKey, item.TrailerName) = Trailer(root);
            if (root.TryGetProperty("watch/providers", out var providers) && providers.TryGetProperty("results", out var providerResults)
                && providerResults.TryGetProperty(country.ToUpperInvariant(), out var local) && local.TryGetProperty("flatrate", out var flat) && flat.ValueKind == JsonValueKind.Array)
            {
                item.Providers = flat.EnumerateArray().Select(p => Text(p, "provider_name")).OfType<string>().Select(ProviderName).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxProviders).ToList();
            }

            return (item, releases);
        }

        /// <summary>
        /// Gets the main name of a streaming provider: „Amazon Prime Video with Ads“ → „Amazon Prime Video“,
        /// „HBO Max Amazon Channel“ → „HBO Max“, „Netflix Standard with Ads“ → „Netflix“, „Paramount Plus“ → „Paramount+“,
        /// „Disney Plus“ → „Disney+“, „Anime Digital Network“ → „ADN“.
        /// </summary>
        /// <param name="name">Name at TMDB.</param>
        /// <returns>The main name.</returns>
        public static string ProviderName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            var result = name.Trim();
            foreach (var suffix in new[] { " Standard with Ads", " Basic with Ads", " with Ads", " Amazon Channel", " Apple TV Channel", " Roku Premium Channel", " via Prime Video" })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && result.Length > suffix.Length)
                {
                    result = result[..^suffix.Length].Trim();
                }
            }

            // „… Plus“ wie im deutschen Angebot als „…+“ (Paramount+, Disney+, Apple TV+, RTL+)
            if (result.EndsWith(" Plus", StringComparison.OrdinalIgnoreCase) && result.Length > 5)
            {
                result = result[..^5].TrimEnd() + "+";
            }

            return ProviderNames.TryGetValue(result, out var known) ? known : result;
        }

        /// <summary>
        /// Gets whether a text is written in Latin script (letters only; digits and signs do not count).
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>True if all letters are Latin.</returns>
        public static bool IsLatin(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // Lateinische Buchstaben einschließlich Umlauten und Akzenten (bis U+024F und U+1E00–U+1EFF)
            return text.Where(char.IsLetter).All(c => c <= '\u024F' || (c >= '\u1E00' && c <= '\u1EFF'));
        }

        /// <summary>
        /// Gets whether a title is fit for a German row: a title in Latin script and a German text (or at least a German title
        /// different from the original), and enough interest – released ones with at least <paramref name="minVotes"/> votes,
        /// upcoming ones with at least <paramref name="minPopularity"/> popularity.
        /// </summary>
        /// <param name="item">The title.</param>
        /// <param name="released">True if already released.</param>
        /// <param name="minVotes">Minimum votes for released titles.</param>
        /// <param name="minPopularity">Minimum popularity for upcoming titles.</param>
        /// <returns>True if it fits.</returns>
        public static bool IsRelevant(DiscoverItem item, bool released, int minVotes, double minPopularity)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!IsLatin(item.Title))
            {
                return false;
            }

            var translated = !string.IsNullOrWhiteSpace(item.Overview) || !string.Equals(item.Title, item.OriginalTitle, StringComparison.OrdinalIgnoreCase);
            if (!translated)
            {
                return false;
            }

            return released ? item.VoteCount >= minVotes : item.Popularity >= minPopularity;
        }

        /// <summary>
        /// Gets the order score of a „new this year“ title: popularity (logarithmic) the more, the nearer the date is to today.
        /// </summary>
        /// <param name="item">The title.</param>
        /// <param name="day">Its date.</param>
        /// <param name="today">Today.</param>
        /// <returns>The score (higher first).</returns>
        public static double Score(DiscoverItem item, DateOnly day, DateOnly today)
        {
            ArgumentNullException.ThrowIfNull(item);
            var distance = Math.Abs(day.DayNumber - today.DayNumber);
            return Math.Log(1 + Math.Max(0, item.Popularity) + (item.VoteCount / 50.0)) / (1 + (distance / 60.0));
        }

        /// <summary>
        /// Gets the German cinema start: theatrical (type 3), otherwise limited (type 2).
        /// </summary>
        /// <param name="releases">Release dates of the country by type (0 = primary).</param>
        /// <returns>The day, or null.</returns>
        public static DateOnly? CinemaStart(IReadOnlyList<(int Type, DateOnly Date)> releases)
        {
            ArgumentNullException.ThrowIfNull(releases);
            var theatrical = releases.Where(r => r.Type == 3).Select(r => (DateOnly?)r.Date).OrderBy(d => d).FirstOrDefault();
            return theatrical ?? releases.Where(r => r.Type == 2).Select(r => (DateOnly?)r.Date).OrderBy(d => d).FirstOrDefault();
        }

        /// <summary>
        /// Gets the German release of a movie: cinema (2/3), otherwise digital (4), otherwise disc (5), otherwise TV (6), otherwise the primary date.
        /// </summary>
        /// <param name="releases">Release dates of the country by type (0 = primary).</param>
        /// <returns>The day, or null.</returns>
        public static DateOnly? LocalRelease(IReadOnlyList<(int Type, DateOnly Date)> releases)
        {
            ArgumentNullException.ThrowIfNull(releases);
            foreach (var types in new[] { new[] { 2, 3 }, new[] { 4 }, new[] { 5 }, new[] { 6 }, new[] { 0 } })
            {
                var day = releases.Where(r => types.Contains(r.Type)).Select(r => (DateOnly?)r.Date).OrderBy(d => d).FirstOrDefault();
                if (day != null)
                {
                    return day;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the line for a „new this year“ card: „Erschienen am 12.09.“ / „Erscheint am Fr. 14.11.“ (series: „Gestartet am …“ / „Startet …“).
        /// </summary>
        /// <param name="day">The date.</param>
        /// <param name="today">Today.</param>
        /// <param name="series">True for a series.</param>
        /// <returns>The text.</returns>
        public static string NewText(DateOnly day, DateOnly today, bool series)
        {
            if (day <= today)
            {
                var when = day == today ? "heute" : day == today.AddDays(-1) ? "gestern" : "am " + day.ToString("dd.MM.", CultureInfo.InvariantCulture);
                return (series ? "Gestartet " : "Erschienen ") + when;
            }

            var text = UpcomingService.DayText(day, today);
            var on = text is "heute" or "morgen" ? text : "am " + text;
            return (series ? "Startet " : "Erscheint ") + on;
        }

        // Trailer auf YouTube: deutsch vor englisch, offizieller Trailer vor Teaser
        private static (string? Key, string? Name) Trailer(JsonElement root)
        {
            if (!root.TryGetProperty("videos", out var videos) || !videos.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return (null, null);
            }

            var best = results.EnumerateArray()
                .Where(v => string.Equals(Text(v, "site"), "YouTube", StringComparison.OrdinalIgnoreCase) && Text(v, "key") != null)
                .Select(v => (Key: Text(v, "key")!, Name: Text(v, "name"), Type: Text(v, "type"), Language: Text(v, "iso_639_1"), Official: v.TryGetProperty("official", out var o) && o.ValueKind == JsonValueKind.True))
                .OrderBy(v => v.Language == "de" ? 0 : 1)
                .ThenBy(v => v.Type == "Trailer" ? 0 : v.Type == "Teaser" ? 1 : 2)
                .ThenBy(v => v.Official ? 0 : 1)
                .FirstOrDefault();
            return best.Key == null ? (null, null) : (best.Key, best.Name);
        }

        private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
    }
}
