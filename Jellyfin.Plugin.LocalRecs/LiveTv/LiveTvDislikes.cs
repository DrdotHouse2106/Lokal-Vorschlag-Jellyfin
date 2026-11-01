using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// „Weniger davon“ eines Benutzers für „Heute im TV für dich“: Sendungen (normalisierter Titel) mit „weniger“
    /// (deutlich abgewertet) oder „nie“ (ausgeblendet) sowie Genres mit „genre_weniger“ (leicht abgewertet).
    /// </summary>
    public class LiveTvDislikes
    {
        /// <summary>Kind: show clearly less often.</summary>
        public const string Less = "weniger";

        /// <summary>Kind: genre slightly less often.</summary>
        public const string GenreLess = "genre_weniger";

        /// <summary>Kind: never show this show.</summary>
        public const string Never = "nie";

        /// <summary>Factor for shows marked „weniger“.</summary>
        public const float LessFactor = 0.3f;

        /// <summary>Factor for programs in a genre marked „genre_weniger“.</summary>
        public const float GenreFactor = 0.6f;

        // Kategorien der Programmzeitschrift, die kein Genre sind
        private static readonly HashSet<string> GuideCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Series", "Serie", "Movie", "Film", "Sports", "Sport", "News", "Kids", "Children", "Show", "Program", "Programm"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvDislikes"/> class.
        /// </summary>
        /// <param name="titles">Normalized title → kind („weniger“ or „nie“).</param>
        /// <param name="genres">Normalized genres marked „genre_weniger“.</param>
        public LiveTvDislikes(IReadOnlyDictionary<string, string> titles, IReadOnlySet<string> genres)
        {
            Titles = titles ?? throw new ArgumentNullException(nameof(titles));
            Genres = genres ?? throw new ArgumentNullException(nameof(genres));
        }

        /// <summary>Gets an empty instance.</summary>
        public static LiveTvDislikes Empty { get; } = new LiveTvDislikes(new Dictionary<string, string>(), new HashSet<string>());

        /// <summary>Gets the shows: normalized title → kind.</summary>
        public IReadOnlyDictionary<string, string> Titles { get; }

        /// <summary>Gets the genres marked „genre_weniger“ (normalized).</summary>
        public IReadOnlySet<string> Genres { get; }

        /// <summary>Gets a value indicating whether nothing is marked.</summary>
        public bool IsEmpty => Titles.Count == 0 && Genres.Count == 0;

        /// <summary>
        /// Gets the genre for „genre_weniger“ from the genres of a program: the first real genre, not a category of the
        /// guide such as „Series“, „Movie“, „Sports“, „News“ or „Kids“.
        /// </summary>
        /// <param name="genres">Genres of the program.</param>
        /// <returns>The genre, or null.</returns>
        public static string? MainGenre(IEnumerable<string>? genres) =>
            genres?.Select(g => g?.Trim()).FirstOrDefault(g => !string.IsNullOrEmpty(g) && !GuideCategories.Contains(g));

        /// <summary>
        /// Gets a value indicating whether a show is hidden („nie“).
        /// </summary>
        /// <param name="titleKey">Normalized title.</param>
        /// <returns>True if hidden.</returns>
        public bool IsHidden(string? titleKey) =>
            !string.IsNullOrEmpty(titleKey) && Titles.TryGetValue(titleKey, out var kind) && kind == Never;

        /// <summary>
        /// Gets a value indicating whether a show is marked at all („weniger“ or „nie“).
        /// </summary>
        /// <param name="titleKey">Normalized title.</param>
        /// <returns>True if marked.</returns>
        public bool IsMarked(string? titleKey) => !string.IsNullOrEmpty(titleKey) && Titles.ContainsKey(titleKey);

        /// <summary>
        /// Gets the factor for the score of a program: „weniger“ × <see cref="LessFactor"/>, a marked genre × <see cref="GenreFactor"/>.
        /// </summary>
        /// <param name="titleKey">Normalized title.</param>
        /// <param name="genres">Genres of the program.</param>
        /// <returns>The factor (1 if nothing applies).</returns>
        public float Factor(string? titleKey, IEnumerable<string>? genres)
        {
            var factor = 1f;
            if (!string.IsNullOrEmpty(titleKey) && Titles.TryGetValue(titleKey, out var kind) && kind == Less)
            {
                factor *= LessFactor;
            }

            if (genres != null && Genres.Count > 0 && genres.Any(g => Genres.Contains(LiveTvText.NormalizeFeature(g))))
            {
                factor *= GenreFactor;
            }

            return factor;
        }
    }
}
