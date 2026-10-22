using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LocalRecs.Seasonal
{
    /// <summary>
    /// Jahreszeiten und Feiertage für „Passend zur Jahreszeit“: Zeitfenster und woran ein Titel zu erkennen ist –
    /// TMDB-Stichwörter (bzw. gleichlautende Tags in Jellyfin) und ganze Wörter im Titel. Teilwörter zählen nicht
    /// („Adventure“ ist kein Advent).
    /// </summary>
    public static class SeasonCalendar
    {
        /// <summary>Christmas: first Sunday of Advent to 26 December.</summary>
        public const string Christmas = "weihnachten";

        /// <summary>New Year's Eve: 27 December to 1 January.</summary>
        public const string NewYear = "silvester";

        /// <summary>Halloween: 15 to 31 October.</summary>
        public const string Halloween = "halloween";

        /// <summary>Easter: Good Friday to Easter Monday.</summary>
        public const string Easter = "ostern";

        /// <summary>Valentine's Day: 7 to 14 February (optional).</summary>
        public const string Valentine = "valentinstag";

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        private static readonly Dictionary<string, Season> Seasons = new Dictionary<string, Season>(StringComparer.Ordinal)
        {
            [Christmas] = new Season(
                Christmas,
                "Weihnachtsfilme",
                new[] { "christmas", "christmas eve", "christmas party", "christmas tree", "christmas present", "christmas carol", "santa claus", "nativity", "weihnachten" },
                new[] { "weihnacht", "christmas", "xmas", "x-mas", "nikolaus", "santa claus" },
                new Regex(@"\b(weihnacht\w*|christmas\w*|xmas|x-mas|nikolaus|santa claus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)),
            [NewYear] = new Season(
                NewYear,
                "Silvester",
                new[] { "new year's eve", "new year", "new year's party", "silvester" },
                new[] { "silvester", "new year", "neujahr" },
                new Regex(@"\b(silvester|new year'?s eve|neujahr\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)),
            [Halloween] = new Season(
                Halloween,
                "Halloween",
                new[] { "halloween", "trick or treating", "jack-o'-lantern" },
                new[] { "halloween" },
                new Regex(@"\bhalloween\w*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)),
            [Easter] = new Season(
                Easter,
                "Ostern",
                new[] { "easter", "easter bunny", "ostern" },
                new[] { "ostern", "osterhase", "easter" },
                new Regex(@"\b(ostern|osterhase\w*|easter)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)),
            [Valentine] = new Season(
                Valentine,
                "Zum Valentinstag",
                new[] { "valentine's day", "valentinstag" },
                new[] { "valentin" },
                new Regex(@"\b(valentinstag\w*|valentine'?s day)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout))
        };

        /// <summary>
        /// Gets a season by ID.
        /// </summary>
        /// <param name="id">The ID.</param>
        /// <returns>The season, or null.</returns>
        public static Season? Get(string? id) => id != null && Seasons.TryGetValue(id, out var season) ? season : null;

        /// <summary>
        /// Gets Easter Sunday of a year (Gregorian, Gauss/anonymous algorithm).
        /// </summary>
        /// <param name="year">The year.</param>
        /// <returns>Easter Sunday.</returns>
        public static DateOnly EasterSunday(int year)
        {
            var a = year % 19;
            var b = year / 100;
            var c = year % 100;
            var d = b / 4;
            var e = b % 4;
            var f = (b + 8) / 25;
            var g = (b - f + 1) / 3;
            var h = ((19 * a) + b - d - g + 15) % 30;
            var i = c / 4;
            var k = c % 4;
            var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
            var m = (a + (11 * h) + (22 * l)) / 451;
            var month = (h + l - (7 * m) + 114) / 31;
            var day = ((h + l - (7 * m) + 114) % 31) + 1;
            return new DateOnly(year, month, day);
        }

        /// <summary>
        /// Gets the first Sunday of Advent of a year (the fourth Sunday before Christmas).
        /// </summary>
        /// <param name="year">The year.</param>
        /// <returns>First Sunday of Advent.</returns>
        public static DateOnly FirstAdvent(int year)
        {
            var fourth = new DateOnly(year, 12, 24);
            while (fourth.DayOfWeek != DayOfWeek.Sunday)
            {
                fourth = fourth.AddDays(-1);
            }

            return fourth.AddDays(-21);
        }

        /// <summary>
        /// Gets the season of a day, or null outside all windows.
        /// </summary>
        /// <param name="day">The local day.</param>
        /// <param name="withValentine">True to include Valentine's Day.</param>
        /// <returns>The season, or null.</returns>
        public static Season? SeasonOf(DateOnly day, bool withValentine)
        {
            if ((day.Month == 12 && day.Day >= 27) || (day.Month == 1 && day.Day == 1))
            {
                return Seasons[NewYear];
            }

            if (day >= FirstAdvent(day.Year) && day <= new DateOnly(day.Year, 12, 26))
            {
                return Seasons[Christmas];
            }

            if (day.Month == 10 && day.Day >= 15)
            {
                return Seasons[Halloween];
            }

            var easter = EasterSunday(day.Year);
            if (day >= easter.AddDays(-2) && day <= easter.AddDays(1))
            {
                return Seasons[Easter];
            }

            if (withValentine && day.Month == 2 && day.Day >= 7 && day.Day <= 14)
            {
                return Seasons[Valentine];
            }

            return null;
        }

        /// <summary>
        /// A season: ID, row title, TMDB keywords (lower case) and the title pattern.
        /// </summary>
        public sealed class Season
        {
            private readonly HashSet<string> _keywords;
            private readonly Regex _title;

            /// <summary>
            /// Initializes a new instance of the <see cref="Season"/> class.
            /// </summary>
            /// <param name="id">The ID.</param>
            /// <param name="title">Title of the row.</param>
            /// <param name="keywords">TMDB keywords and tags that mark a title.</param>
            /// <param name="stems">Parts of titles to search the library for (then checked with the pattern).</param>
            /// <param name="titlePattern">Whole words in titles that mark a title.</param>
            public Season(string id, string title, IEnumerable<string> keywords, IReadOnlyList<string> stems, Regex titlePattern)
            {
                Id = id;
                Title = title;
                Stems = stems;
                _keywords = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);
                _title = titlePattern;
            }

            /// <summary>Gets the ID.</summary>
            public string Id { get; }

            /// <summary>Gets the title of the row.</summary>
            public string Title { get; }

            /// <summary>Gets the parts of titles to search the library for.</summary>
            public IReadOnlyList<string> Stems { get; }

            /// <summary>
            /// Gets whether one of the keywords or tags marks the season (whole keyword, not part of it).
            /// </summary>
            /// <param name="keywords">TMDB keywords or Jellyfin tags.</param>
            /// <returns>True if it matches.</returns>
            public bool MatchesKeywords(IEnumerable<string>? keywords) => keywords != null && keywords.Any(k => k != null && _keywords.Contains(k.Trim()));

            /// <summary>
            /// Gets whether a title contains a word of the season.
            /// </summary>
            /// <param name="title">The title.</param>
            /// <returns>True if it matches.</returns>
            public bool MatchesTitle(string? title)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    return false;
                }

                try
                {
                    return _title.IsMatch(title);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }
        }
    }
}
