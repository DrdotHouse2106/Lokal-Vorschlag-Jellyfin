using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// Liest die öffentlichen Ähnlichkeitslisten von TMDB („recommendations“, „similar“) und Trakt („related“)
    /// und führt sie zu einer gewichteten Liste zusammen.
    /// </summary>
    public static class SimilarityParser
    {
        /// <summary>Weight of TMDB recommendations (based on what people watch together).</summary>
        public const double TmdbRecommendationsWeight = 1.0;

        /// <summary>Weight of Trakt related titles.</summary>
        public const double TraktRelatedWeight = 0.8;

        /// <summary>Weight of TMDB similar titles (based on genres and keywords).</summary>
        public const double TmdbSimilarWeight = 0.6;

        /// <summary>
        /// Reads the TMDB IDs of a TMDB list response (<c>{"results":[{"id":…},…]}</c>).
        /// </summary>
        /// <param name="json">Response body.</param>
        /// <returns>TMDB IDs in list order.</returns>
        public static IReadOnlyList<string> ParseTmdb(string json)
        {
            var result = new List<string>();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in results.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    result.Add(id.GetInt64().ToString(CultureInfo.InvariantCulture));
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the TMDB IDs of a Trakt "related" response (<c>[{"ids":{"tmdb":…}},…]</c>).
        /// </summary>
        /// <param name="json">Response body.</param>
        /// <returns>TMDB IDs in list order (entries without TMDB ID are skipped).</returns>
        public static IReadOnlyList<string> ParseTrakt(string json)
        {
            var result = new List<string>();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Object
                    && ids.TryGetProperty("tmdb", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number)
                {
                    result.Add(tmdb.GetInt64().ToString(CultureInfo.InvariantCulture));
                }
            }

            return result;
        }

        /// <summary>
        /// Weight of a list position: 1 for the first entry, falling linearly to 0.2 at the end of a list of 20.
        /// </summary>
        /// <param name="index">Position (0 = first).</param>
        /// <returns>Weight between 0.2 and 1.</returns>
        public static double PositionWeight(int index) => Math.Max(0.2, 1.0 - (index * 0.04));

        /// <summary>
        /// Merges the lists of the sources: per title the strongest mention counts, each further source adds a little.
        /// </summary>
        /// <param name="kind">"movie" or "tv" (Trakt and TMDB list titles of the same kind).</param>
        /// <param name="selfTmdbId">TMDB ID of the title itself (never similar to itself).</param>
        /// <param name="lists">Lists with their source weight.</param>
        /// <returns>Similar titles, most similar first (at most 40).</returns>
        public static List<SimilarTitle> Merge(string kind, string selfTmdbId, IEnumerable<(IReadOnlyList<string> Ids, double SourceWeight)> lists)
        {
            ArgumentNullException.ThrowIfNull(lists);
            var weights = new Dictionary<string, (double Best, double Sum)>(StringComparer.Ordinal);
            foreach (var (ids, sourceWeight) in lists)
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (string.Equals(id, selfTmdbId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var weight = sourceWeight * PositionWeight(i);
                    var current = weights.GetValueOrDefault(id);
                    weights[id] = (Math.Max(current.Best, weight), current.Sum + weight);
                }
            }

            return weights
                .Select(w => new SimilarTitle
                {
                    Kind = kind,
                    TmdbId = w.Key,
                    Weight = Math.Min(1.0, w.Value.Best + (0.25 * (w.Value.Sum - w.Value.Best)))
                })
                .OrderByDescending(t => t.Weight)
                .ThenBy(t => t.TmdbId, StringComparer.Ordinal)
                .Take(40)
                .ToList();
        }
    }
}
