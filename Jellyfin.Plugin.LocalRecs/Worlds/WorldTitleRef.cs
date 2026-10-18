using System;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// A title in the world list, identified by provider IDs (the same on every server).
    /// </summary>
    public class WorldTitleRef
    {
        /// <summary>Gets or sets the TMDB ID.</summary>
        public string? Tmdb { get; set; }

        /// <summary>Gets or sets the IMDb ID (e.g. "tt0371746").</summary>
        public string? Imdb { get; set; }

        /// <summary>Gets or sets the TVDB ID (series).</summary>
        public string? Tvdb { get; set; }

        /// <summary>Gets or sets the type: "film", "serie" or null (both). TMDB IDs of movies and series overlap.</summary>
        public string? Type { get; set; }

        /// <summary>Gets or sets part of a collection name: all titles of matching collections (only useful in "ausschluss").</summary>
        public string? Collection { get; set; }

        /// <summary>Gets or sets the title (only for readability).</summary>
        public string? Name { get; set; }

        /// <summary>
        /// Checks whether the type allows movies or series.
        /// </summary>
        /// <param name="isSeries">Whether the library item is a series.</param>
        /// <returns>True if the type fits.</returns>
        public bool FitsType(bool isSeries)
        {
            if (string.IsNullOrEmpty(Type))
            {
                return true;
            }

            return isSeries
                ? Type.StartsWith("serie", StringComparison.Ordinal) || Type == "series" || Type == "tv"
                : Type == "film" || Type == "movie";
        }
    }
}
