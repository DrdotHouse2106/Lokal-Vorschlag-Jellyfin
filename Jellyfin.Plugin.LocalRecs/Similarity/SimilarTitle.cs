namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// A title that a public source (TMDB, Trakt) names as similar to another title.
    /// </summary>
    public class SimilarTitle
    {
        /// <summary>Gets or sets the kind: "movie" or "tv".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Gets or sets the TMDB ID.</summary>
        public string TmdbId { get; set; } = string.Empty;

        /// <summary>Gets or sets the weight between 0 and 1 (higher = more similar, from source and position in the list).</summary>
        public double Weight { get; set; }

        /// <summary>
        /// Gets the cache key of a title, e.g. "movie:603".
        /// </summary>
        /// <param name="kind">"movie" or "tv".</param>
        /// <param name="tmdbId">TMDB ID.</param>
        /// <returns>The key.</returns>
        public static string Key(string kind, string tmdbId) => kind + ":" + tmdbId;
    }
}
