using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Hilfsfunktionen, um Sendungstitel und Genres aus dem EPG vergleichbar zu machen.
    /// </summary>
    public static class LiveTvText
    {
        private static readonly char[] ListSeparators = { ',', ';', '|' };
        private static readonly string[] TitleSeparators = { ":", " - ", " – " };

        // Reihenfolge ist wichtig: speziellere Sportarten zuerst, Fußball zuletzt,
        // weil z. B. „Handball: Bundesliga“ auch „Bundesliga“ enthält.
        private static readonly (string Sport, string[] Keywords)[] SportKeywords =
        {
            ("handball", new[] { "handball" }),
            ("american football", new[] { "american football", "nfl", "super bowl" }),
            ("eishockey", new[] { "eishockey", "ice hockey", "nhl" }),
            ("basketball", new[] { "basketball", "nba", "euroleague" }),
            ("tennis", new[] { "tennis", "atp", "wta", "wimbledon" }),
            ("golf", new[] { "golf", "ryder cup", "pga" }),
            ("motorsport", new[] { "motorsport", "formel 1", "formel1", "formula 1", "motogp", "dtm", "nascar", "indycar", "rallye" }),
            ("darts", new[] { "darts" }),
            ("kampfsport", new[] { "kampfsport", "boxen", "boxing", "ufc", "mma", "wrestling", "kickboxen" }),
            ("radsport", new[] { "radsport", "tour de france", "giro d'italia", "vuelta" }),
            ("wintersport", new[] { "wintersport", "biathlon", "skispringen", "ski alpin", "langlauf" }),
            ("volleyball", new[] { "volleyball" }),
            ("fußball", new[]
            {
                "fußball", "fussball", "bundesliga", "champions league", "europa league", "conference league",
                "dfb-pokal", "premier league", "la liga", "serie a", "ligue 1", "eredivisie", "länderspiel",
                "uefa", "fifa", "nations league"
            })
        };

        /// <summary>
        /// Normalizes a title so that different broadcasts of the same show match,
        /// e.g. "Fußball: 1. Bundesliga, 7. Spieltag" becomes "fußball" and "Tatort: Der Fall" becomes "tatort".
        /// </summary>
        /// <param name="title">Title or series name.</param>
        /// <returns>The normalized key (empty if the title is empty).</returns>
        public static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var text = title.Trim();
            foreach (var separator in TitleSeparators)
            {
                var index = text.IndexOf(separator, StringComparison.Ordinal);
                if (index >= 3)
                {
                    text = text.Substring(0, index);
                }
            }

            text = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
            return text;
        }

        /// <summary>
        /// Normalizes a genre or person name.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>The normalized value.</returns>
        public static string NormalizeFeature(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Gets the title key of a watched broadcast (series name if available, else title).
        /// </summary>
        /// <param name="entry">Watched broadcast.</param>
        /// <returns>The normalized key.</returns>
        public static string TitleKey(LiveTvWatchEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return NormalizeTitle(string.IsNullOrWhiteSpace(entry.SeriesName) ? entry.Title : entry.SeriesName);
        }

        /// <summary>
        /// Parses a comma separated genre list from the configuration.
        /// </summary>
        /// <param name="list">Comma separated list.</param>
        /// <returns>Normalized genres.</returns>
        public static HashSet<string> ParseList(string? list)
        {
            return (list ?? string.Empty)
                .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeFeature)
                .Where(g => g.Length > 0)
                .ToHashSet();
        }

        /// <summary>
        /// Detects the kind of sport from genres and texts (title, episode title, series name).
        /// Genres are checked first because they are the most reliable source in German EPGs
        /// (e.g. "Fußball", "1. Bundesliga", "Handball").
        /// </summary>
        /// <param name="genres">Genres of the program.</param>
        /// <param name="texts">Title, episode title, series name.</param>
        /// <returns>The sport (e.g. "fußball", "handball") or null.</returns>
        public static string? DetectSport(IEnumerable<string>? genres, params string?[] texts)
        {
            var genreList = (genres ?? Enumerable.Empty<string>()).Select(NormalizeFeature).Where(g => g.Length > 0).ToList();
            var fromGenres = FindSport(string.Join(" | ", genreList));
            if (fromGenres != null)
            {
                return fromGenres;
            }

            // Titel nur auswerten, wenn die Sendung laut Genre Sport ist (oder gar kein Genre hat),
            // damit z. B. „Tatort: Bundesliga-Mord“ nicht als Fußball gilt
            if (genreList.Count > 0 && !IsSportsGenre(genreList))
            {
                return null;
            }

            return FindSport(string.Join(" | ", (texts ?? Array.Empty<string?>()).Select(NormalizeFeature)));
        }

        /// <summary>
        /// Checks whether the genres describe a sports broadcast (e.g. "Sport", "Fußball", "Sports event").
        /// </summary>
        /// <param name="genres">Genres of a program.</param>
        /// <returns>True for sports.</returns>
        public static bool IsSportsGenre(IEnumerable<string>? genres)
        {
            return (genres ?? Enumerable.Empty<string>()).Any(g => NormalizeFeature(g).Contains("sport", StringComparison.Ordinal));
        }

        /// <summary>
        /// Checks whether any of the genres is excluded.
        /// </summary>
        /// <param name="genres">Genres of a program.</param>
        /// <param name="excluded">Excluded genres (normalized).</param>
        /// <returns>True if the program should be ignored.</returns>
        public static bool IsExcluded(IEnumerable<string>? genres, HashSet<string> excluded)
        {
            ArgumentNullException.ThrowIfNull(excluded);
            return excluded.Count > 0 && (genres ?? Enumerable.Empty<string>()).Any(g => excluded.Contains(NormalizeFeature(g)));
        }

        private static string? FindSport(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            foreach (var (sport, keywords) in SportKeywords)
            {
                foreach (var keyword in keywords)
                {
                    // Kurze Abkürzungen (NFL, ATP, …) nur als ganzes Wort, damit sie nicht in anderen Wörtern stecken
                    var found = keyword.Length <= 4
                        ? Regex.IsMatch(text, @"(?<![\p{L}\d])" + Regex.Escape(keyword) + @"(?![\p{L}\d])")
                        : text.Contains(keyword, StringComparison.Ordinal);
                    if (found)
                    {
                        return sport;
                    }
                }
            }

            return null;
        }
    }
}
