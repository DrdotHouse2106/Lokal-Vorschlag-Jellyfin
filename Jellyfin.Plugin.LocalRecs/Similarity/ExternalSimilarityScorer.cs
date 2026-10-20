using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// Rechnet die öffentlichen Ähnlichkeitslisten auf die eigene Bibliothek um: Titel, die TMDB bzw. Trakt als
    /// ähnlich zu gesehenen oder gemochten Titeln nennen, bekommen eine Stärke zwischen 0 und 1 und den Titel,
    /// der am meisten dazu beigetragen hat (für „Ähnlich wie …“).
    /// </summary>
    public static class ExternalSimilarityScorer
    {
        /// <summary>Minimum strength for the reason „Ähnlich wie …“.</summary>
        public const double ReasonThreshold = 0.3;

        /// <summary>Reason for personal Trakt recommendations.</summary>
        public const string TraktReason = "Für dich bei Trakt empfohlen";

        /// <summary>
        /// Gets the kind used by the public sources for a media type.
        /// </summary>
        /// <param name="type">Media type.</param>
        /// <returns>"movie" or "tv".</returns>
        public static string KindOf(MediaType type) => type == MediaType.Series ? "tv" : "movie";

        /// <summary>
        /// Builds the reason text.
        /// </summary>
        /// <param name="seedTitle">Title of the watched or liked item.</param>
        /// <returns>The reason.</returns>
        public static string SimilarTo(string seedTitle) => "Ähnlich wie „" + seedTitle + "“";

        /// <summary>
        /// Maps personal Trakt recommendations onto the library: strength 1 for the first, falling to 0.3.
        /// </summary>
        /// <param name="personal">The recommendations.</param>
        /// <param name="metadata">Library metadata (TMDB IDs).</param>
        /// <returns>Strength by library item.</returns>
        public static Dictionary<Guid, double> PersonalHits(PersonalRecommendations personal, IReadOnlyDictionary<Guid, MediaItemMetadata> metadata)
        {
            ArgumentNullException.ThrowIfNull(personal);
            ArgumentNullException.ThrowIfNull(metadata);
            var rank = new Dictionary<string, double>(StringComparer.Ordinal);
            void Add(IReadOnlyList<string> ids, string kind)
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    rank.TryAdd(SimilarTitle.Key(kind, ids[i]), Math.Max(0.3, 1.0 - (i * 0.015)));
                }
            }

            Add(personal.Movies, "movie");
            Add(personal.Shows, "tv");
            var result = new Dictionary<Guid, double>();
            foreach (var item in metadata.Values)
            {
                if (!string.IsNullOrEmpty(item.TmdbId) && rank.TryGetValue(SimilarTitle.Key(KindOf(item.Type), item.TmdbId), out var strength))
                {
                    result[item.Id] = strength;
                }
            }

            return result;
        }

        /// <summary>
        /// Computes the strength per library item.
        /// </summary>
        /// <param name="seeds">Watched or liked items of the user, with their own weight (e.g. 1 for a favorite).</param>
        /// <param name="metadata">Library metadata (TMDB IDs and titles).</param>
        /// <param name="lookup">Similar titles per seed kind and TMDB ID, or null if unknown.</param>
        /// <returns>Hits by library item (seeds themselves are not included).</returns>
        public static Dictionary<Guid, ExternalHit> Compute(
            IEnumerable<(Guid ItemId, double Weight)> seeds,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            Func<string, string, IReadOnlyList<SimilarTitle>?> lookup)
        {
            ArgumentNullException.ThrowIfNull(seeds);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(lookup);

            var byKey = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            foreach (var item in metadata.Values)
            {
                if (string.IsNullOrEmpty(item.TmdbId))
                {
                    continue;
                }

                var key = SimilarTitle.Key(KindOf(item.Type), item.TmdbId);
                if (!byKey.TryGetValue(key, out var list))
                {
                    byKey[key] = list = new List<Guid>();
                }

                list.Add(item.Id);
            }

            var seedList = seeds.Where(s => metadata.ContainsKey(s.ItemId)).ToList();
            var seedIds = seedList.Select(s => s.ItemId).ToHashSet();
            var sums = new Dictionary<Guid, (double Sum, Guid Seed, double Best)>();
            foreach (var (seedId, seedWeight) in seedList)
            {
                var seed = metadata[seedId];
                if (string.IsNullOrEmpty(seed.TmdbId))
                {
                    continue;
                }

                var similar = lookup(KindOf(seed.Type), seed.TmdbId);
                if (similar == null)
                {
                    continue;
                }

                foreach (var title in similar)
                {
                    if (!byKey.TryGetValue(SimilarTitle.Key(title.Kind, title.TmdbId), out var targets))
                    {
                        continue;
                    }

                    var contribution = title.Weight * seedWeight;
                    foreach (var target in targets.Where(t => !seedIds.Contains(t)))
                    {
                        var current = sums.GetValueOrDefault(target);
                        sums[target] = contribution > current.Best
                            ? (current.Sum + contribution, seedId, contribution)
                            : (current.Sum + contribution, current.Seed, current.Best);
                    }
                }
            }

            // Mehrere Nennungen verstärken, sättigen aber: 1 – e^(–Summe)
            return sums.ToDictionary(
                s => s.Key,
                s => new ExternalHit(1.0 - Math.Exp(-s.Value.Sum), s.Value.Seed));
        }
    }
}
