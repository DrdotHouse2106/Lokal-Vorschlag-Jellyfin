using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Wann ein Benutzer was schaut: Wiedergaben nach Stunde (UTC), Art (Film, Serie, Live-TV) und Genre.
    /// Die Tageszeit wird erst beim Abfragen mit der Zeitzone des Benutzers bestimmt.
    /// </summary>
    public class DaypartProfile
    {
        /// <summary>Kind: movie.</summary>
        public const string Movies = "movies";

        /// <summary>Kind: series.</summary>
        public const string Series = "series";

        /// <summary>Kind: live TV.</summary>
        public const string LiveTv = "livetv";

        /// <summary>Plays needed in a daypart before it changes anything.</summary>
        public const int MinPlays = 15;

        private static readonly string[] Kinds = { Movies, Series, LiveTv };
        private static readonly string[] Labels = { "morgens", "tagsüber", "abends", "nachts" };
        private static readonly HashSet<string> PriorDaytime = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Familie", "Family", "Kinder", "Kids", "Children", "Animation", "Zeichentrick" };
        private static readonly HashSet<string> PriorNight = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Horror", "Thriller", "Mystery" };

        private readonly Dictionary<string, int[]> _kindHours = Kinds.ToDictionary(k => k, _ => new int[24], StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _genreHours = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the total number of plays.</summary>
        public int Total { get; private set; }

        /// <summary>Gets the watch rhythm (runtime by weekday evening, usual bedtime).</summary>
        public WatchRhythm Rhythm { get; } = new WatchRhythm();

        /// <summary>
        /// Gets the daypart of a local hour: 0 = morning (5–11), 1 = daytime (11–17), 2 = evening (17–22), 3 = night (22–5).
        /// </summary>
        /// <param name="localHour">Local hour (0–23).</param>
        /// <returns>The daypart.</returns>
        public static int DaypartOf(int localHour) => localHour switch
        {
            >= 5 and < 11 => 0,
            >= 11 and < 17 => 1,
            >= 17 and < 22 => 2,
            _ => 3
        };

        /// <summary>
        /// Gets the German name of a daypart.
        /// </summary>
        /// <param name="daypart">Daypart (0–3).</param>
        /// <returns>The name.</returns>
        public static string Label(int daypart) => Labels[Math.Clamp(daypart, 0, 3)];

        /// <summary>
        /// Default for profiles with too little history: daytime family and children, at night horror and thriller (0 or 0.5).
        /// </summary>
        /// <param name="genres">Genres of the title.</param>
        /// <param name="daypart">Daypart.</param>
        /// <returns>Score between 0 and 0.5.</returns>
        public static double PriorGenreScore(IEnumerable<string>? genres, int daypart)
        {
            if (genres == null)
            {
                return 0;
            }

            var set = daypart switch
            {
                1 => PriorDaytime,
                3 => PriorNight,
                _ => null
            };
            return set != null && genres.Any(g => g != null && set.Contains(g)) ? 0.5 : 0;
        }

        /// <summary>
        /// Gets the daypart of a time for a UTC offset.
        /// </summary>
        /// <param name="utc">Time (UTC).</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone (e.g. 120 for CEST).</param>
        /// <returns>The daypart.</returns>
        public static int DaypartAt(DateTime utc, int utcOffsetMinutes) => DaypartOf(utc.AddMinutes(utcOffsetMinutes).Hour);

        /// <summary>
        /// Adds a play.
        /// </summary>
        /// <param name="utc">Time of the play (UTC).</param>
        /// <param name="kind">Kind (<see cref="Movies"/>, <see cref="Series"/>, <see cref="LiveTv"/>).</param>
        /// <param name="genres">Genres of the title.</param>
        public void Add(DateTime utc, string kind, IEnumerable<string>? genres)
        {
            if (!_kindHours.TryGetValue(kind, out var hours))
            {
                return;
            }

            var hour = utc.Hour;
            hours[hour]++;
            Total++;
            foreach (var genre in (genres ?? Array.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_genreHours.TryGetValue(genre, out var genreHours))
                {
                    _genreHours[genre] = genreHours = new int[24];
                }

                genreHours[hour]++;
            }
        }

        /// <summary>
        /// Gets the number of plays in a daypart.
        /// </summary>
        /// <param name="daypart">Daypart.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>Number of plays.</returns>
        public int PlaysIn(int daypart, int utcOffsetMinutes) => _kindHours.Values.Sum(h => InDaypart(h, daypart, utcOffsetMinutes));

        /// <summary>
        /// Orders the kinds by how typical they are for the daypart (share in the daypart compared with the share overall).
        /// Without enough plays in the daypart: movies, series, live TV.
        /// </summary>
        /// <param name="daypart">Daypart.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>The kinds, most typical first.</returns>
        public IReadOnlyList<string> KindOrder(int daypart, int utcOffsetMinutes)
        {
            var inDaypart = PlaysIn(daypart, utcOffsetMinutes);
            if (inDaypart < MinPlays || Total == 0)
            {
                return Kinds;
            }

            return Kinds
                .Select((kind, index) => (Kind: kind, Index: index, Lift: Lift(_kindHours[kind], daypart, utcOffsetMinutes, inDaypart)))
                .OrderByDescending(k => k.Lift)
                .ThenBy(k => k.Index)
                .Select(k => k.Kind)
                .ToList();
        }

        /// <summary>
        /// How typical the genres of a title are for the daypart: 0 (not more than usual) up to 1 (three times as often as usual).
        /// </summary>
        /// <param name="genres">Genres of the title.</param>
        /// <param name="daypart">Daypart.</param>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone.</param>
        /// <returns>Score between 0 and 1 (0 without enough plays in the daypart).</returns>
        public double GenreScore(IEnumerable<string>? genres, int daypart, int utcOffsetMinutes)
        {
            var inDaypart = PlaysIn(daypart, utcOffsetMinutes);
            if (inDaypart < MinPlays || genres == null)
            {
                return 0;
            }

            var best = 0.0;
            foreach (var genre in genres)
            {
                if (string.IsNullOrWhiteSpace(genre) || !_genreHours.TryGetValue(genre, out var hours) || hours.Sum() < 3)
                {
                    continue;
                }

                best = Math.Max(best, Lift(hours, daypart, utcOffsetMinutes, inDaypart));
            }

            return Math.Clamp((best - 1) / 2, 0, 1);
        }

        private static int InDaypart(int[] hours, int daypart, int utcOffsetMinutes)
        {
            var sum = 0;
            for (var utcHour = 0; utcHour < 24; utcHour++)
            {
                if (DaypartAt(new DateTime(2000, 1, 1, utcHour, 0, 0, DateTimeKind.Utc), utcOffsetMinutes) == daypart)
                {
                    sum += hours[utcHour];
                }
            }

            return sum;
        }

        // Anteil in der Tageszeit geteilt durch den Anteil insgesamt (1 = wie sonst auch)
        private double Lift(int[] hours, int daypart, int utcOffsetMinutes, int inDaypart)
        {
            var total = hours.Sum();
            if (total == 0 || inDaypart == 0 || Total == 0)
            {
                return 0;
            }

            var shareInDaypart = (double)InDaypart(hours, daypart, utcOffsetMinutes) / inDaypart;
            var shareOverall = (double)total / Total;
            return shareInDaypart / shareOverall;
        }
    }
}
