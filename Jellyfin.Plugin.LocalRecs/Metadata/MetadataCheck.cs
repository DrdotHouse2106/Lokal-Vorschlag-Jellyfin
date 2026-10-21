using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Metadata
{
    /// <summary>
    /// Bewertet, wie vollständig die Metadaten eines Films oder einer Serie für die Empfehlungen sind.
    /// Schwer wiegt, was die Einordnung trägt (Genres, TMDB/IMDb-ID), mittel, was sie verfeinert (Stichwörter, Besetzung),
    /// leicht der Rest (Jahr, Bewertung, Regie, Beschreibung, Bild).
    /// </summary>
    public static class MetadataCheck
    {
        /// <summary>Missing: genres.</summary>
        public const string Genres = "Genres";

        /// <summary>Missing: TMDB or IMDb ID.</summary>
        public const string ProviderId = "TMDB/IMDb-ID";

        /// <summary>Missing: tags.</summary>
        public const string Tags = "Stichwörter";

        /// <summary>Missing: cast.</summary>
        public const string Cast = "Besetzung";

        /// <summary>Missing: director (movies).</summary>
        public const string Director = "Regie";

        /// <summary>Missing: year.</summary>
        public const string Year = "Jahr";

        /// <summary>Missing: community rating.</summary>
        public const string Rating = "Bewertung";

        /// <summary>Missing: overview.</summary>
        public const string Overview = "Beschreibung";

        /// <summary>Missing: primary image.</summary>
        public const string Image = "Bild";

        /// <summary>Score from which a title counts as having little metadata.</summary>
        public const int LittleFrom = 2;

        /// <summary>All checked fields in display order.</summary>
        public static readonly IReadOnlyList<string> Fields = new[] { Genres, ProviderId, Tags, Cast, Director, Year, Rating, Overview, Image };

        private static readonly HashSet<string> Heavy = new(StringComparer.Ordinal) { Genres, ProviderId };
        private static readonly HashSet<string> Medium = new(StringComparer.Ordinal) { Tags, Cast };

        /// <summary>
        /// Gets the weight of a missing field: 3 heavy, 2 medium, 1 light.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <returns>The weight.</returns>
        public static int Weight(string field) => Heavy.Contains(field) ? 3 : Medium.Contains(field) ? 2 : 1;

        /// <summary>
        /// Gets the score of the missing fields (0 = complete).
        /// </summary>
        /// <param name="missing">Missing fields.</param>
        /// <returns>The score.</returns>
        public static int Score(IEnumerable<string> missing) => (missing ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).Sum(Weight);

        /// <summary>
        /// Gets the level: "zu wenig" if something heavy is missing, "wenig" from <see cref="LittleFrom"/>, otherwise empty.
        /// </summary>
        /// <param name="missing">Missing fields.</param>
        /// <returns>The level.</returns>
        public static string Level(IReadOnlyCollection<string> missing)
        {
            ArgumentNullException.ThrowIfNull(missing);
            if (missing.Any(Heavy.Contains))
            {
                return "zu wenig";
            }

            return Score(missing) >= LittleFrom ? "wenig" : string.Empty;
        }

        /// <summary>
        /// Gets the missing fields of a title.
        /// </summary>
        /// <param name="facts">What the title has.</param>
        /// <returns>The missing fields in display order.</returns>
        public static IReadOnlyList<string> Missing(ItemFacts facts)
        {
            ArgumentNullException.ThrowIfNull(facts);
            var missing = new List<string>();
            Add(missing, facts.GenreCount == 0, Genres);
            Add(missing, !facts.HasProviderId, ProviderId);
            Add(missing, facts.TagCount == 0, Tags);
            Add(missing, facts.ActorCount == 0, Cast);
            Add(missing, facts.IsMovie && facts.DirectorCount == 0, Director);
            Add(missing, !facts.HasYear, Year);
            Add(missing, !facts.HasRating, Rating);
            Add(missing, !facts.HasOverview, Overview);
            Add(missing, !facts.HasImage, Image);
            return missing;
        }

        private static void Add(List<string> missing, bool condition, string field)
        {
            if (condition)
            {
                missing.Add(field);
            }
        }

        /// <summary>
        /// What a title has.
        /// </summary>
        public class ItemFacts
        {
            /// <summary>Gets or sets a value indicating whether it is a movie (otherwise a series).</summary>
            public bool IsMovie { get; set; }

            /// <summary>Gets or sets the number of genres.</summary>
            public int GenreCount { get; set; }

            /// <summary>Gets or sets the number of tags.</summary>
            public int TagCount { get; set; }

            /// <summary>Gets or sets the number of actors.</summary>
            public int ActorCount { get; set; }

            /// <summary>Gets or sets the number of directors.</summary>
            public int DirectorCount { get; set; }

            /// <summary>Gets or sets a value indicating whether a TMDB or IMDb ID is set.</summary>
            public bool HasProviderId { get; set; }

            /// <summary>Gets or sets a value indicating whether the year is set.</summary>
            public bool HasYear { get; set; }

            /// <summary>Gets or sets a value indicating whether a community rating is set.</summary>
            public bool HasRating { get; set; }

            /// <summary>Gets or sets a value indicating whether an overview is set.</summary>
            public bool HasOverview { get; set; }

            /// <summary>Gets or sets a value indicating whether a primary image is set.</summary>
            public bool HasImage { get; set; }
        }
    }
}
