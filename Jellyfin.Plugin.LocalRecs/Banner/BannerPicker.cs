using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.LocalRecs.Banner
{
    /// <summary>
    /// Wählt den Banner des Tages: aus den besten Empfehlungen gewichtet zufällig, aber für einen Benutzer und einen Tag
    /// immer gleich (Startwert aus Datum und Benutzer). Weiter oben empfohlene Titel kommen öfter dran; Titel, die kürzlich
    /// im Banner waren oder lange unbeachtet in den Reihen stehen, seltener.
    /// </summary>
    public static class BannerPicker
    {
        /// <summary>Default number of titles in the banner.</summary>
        public const int DefaultCount = 5;

        /// <summary>Candidates considered (the best recommendations).</summary>
        public const int MaxCandidates = 30;

        /// <summary>
        /// Gets the seed for a user and a day (the same on every server start and every client).
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="day">The local day.</param>
        /// <returns>The seed.</returns>
        public static int Seed(Guid userId, DateOnly day)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "|" + userId.ToString("N")));
            return BitConverter.ToInt32(hash, 0);
        }

        /// <summary>
        /// Gets the base weight of a candidate by its rank: 1 for the first, falling to 0.25 for the last.
        /// </summary>
        /// <param name="rank">Rank (0 = best).</param>
        /// <param name="count">Number of candidates.</param>
        /// <returns>The weight.</returns>
        public static double RankWeight(int rank, int count) => count <= 1 ? 1 : 1 - (0.75 * rank / (count - 1));

        /// <summary>
        /// Picks titles: weighted random sampling without replacement (each title gets the key u^(1/w), the largest keys win).
        /// </summary>
        /// <typeparam name="T">Candidate type.</typeparam>
        /// <param name="ranked">Candidates, best first.</param>
        /// <param name="factor">Extra factor per candidate (1 = normal, smaller = less often), e.g. for recently shown titles.</param>
        /// <param name="count">Number of titles.</param>
        /// <param name="seed">Seed (see <see cref="Seed"/>).</param>
        /// <returns>The picked titles in the order of their keys.</returns>
        public static List<T> Pick<T>(IReadOnlyList<T> ranked, Func<T, double> factor, int count, int seed)
        {
            ArgumentNullException.ThrowIfNull(ranked);
            ArgumentNullException.ThrowIfNull(factor);
            var random = new Random(seed);
            var n = Math.Min(ranked.Count, MaxCandidates);
            return ranked
                .Take(n)
                .Select((item, rank) =>
                {
                    var weight = Math.Max(1e-6, RankWeight(rank, n) * Math.Clamp(factor(item), 0, 1));
                    var u = Math.Max(1e-12, random.NextDouble());
                    return (Item: item, Key: Math.Pow(u, 1 / weight));
                })
                .OrderByDescending(x => x.Key)
                .Take(Math.Max(0, count))
                .Select(x => x.Item)
                .ToList();
        }

        /// <summary>
        /// Interleaves movie and series recommendations by rank (movie 1, series 1, movie 2 …), so both kinds can appear.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First list.</param>
        /// <param name="second">Second list.</param>
        /// <returns>The combined list.</returns>
        public static List<T> Interleave<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            var result = new List<T>(first.Count + second.Count);
            for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
            {
                if (i < first.Count)
                {
                    result.Add(first[i]);
                }

                if (i < second.Count)
                {
                    result.Add(second[i]);
                }
            }

            return result;
        }
    }
}
