using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Interesse aus Suchen und angesehenen Titeln und die Umsortierung der gespeicherten Empfehlungen beim Abruf.
    /// </summary>
    public static class InterestScoring
    {
        /// <summary>Default days after which an interest counts only a third (e^-1).</summary>
        public const double DefaultDecayDays = 30;

        /// <summary>Weight below which an interest is ignored.</summary>
        public const double MinWeight = 0.03;

        /// <summary>Most interests used for one reordering (the strongest).</summary>
        public const int MaxInterests = 300;

        /// <summary>Weight of a search (relative).</summary>
        public const double SearchWeight = 1.0;

        /// <summary>Weight of a viewed title (relative).</summary>
        public const double ViewWeight = 0.6;

        /// <summary>
        /// Weight of an interest by its age: 1 now, e^-1 after <paramref name="decayDays"/> days.
        /// </summary>
        /// <param name="age">Age.</param>
        /// <param name="decayDays">Days after which it counts only a third.</param>
        /// <returns>The weight.</returns>
        public static double Decay(TimeSpan age, double decayDays = DefaultDecayDays) => Math.Exp(-Math.Max(0, age.TotalDays) / Math.Max(1, decayDays));

        /// <summary>
        /// Combines repeated interest in the same title: the weights add up, at most to 1.
        /// </summary>
        /// <param name="weights">Weights of the single entries.</param>
        /// <returns>The combined weight.</returns>
        public static double Combine(IEnumerable<double> weights)
        {
            ArgumentNullException.ThrowIfNull(weights);
            return Math.Min(1, weights.Sum());
        }

        /// <summary>
        /// Similarity of two titles by genres and tags (Jaccard), 1 for the same title.
        /// </summary>
        /// <param name="a">Genres and tags of the first title.</param>
        /// <param name="b">Genres and tags of the second title.</param>
        /// <returns>Similarity between 0 and 1.</returns>
        public static double Similarity(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            if (a.Count == 0 || b.Count == 0)
            {
                return 0;
            }

            var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
            var intersection = b.Distinct(StringComparer.OrdinalIgnoreCase).Count(setA.Contains);
            var union = setA.Count + b.Distinct(StringComparer.OrdinalIgnoreCase).Count() - intersection;
            return union == 0 ? 0 : (double)intersection / union;
        }

        /// <summary>
        /// Sorts a list again: position in the stored order (1 for the first, falling to 0) plus the bonus. Stable for equal values.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="items">Items in stored order.</param>
        /// <param name="bonus">Bonus per item.</param>
        /// <returns>The new order.</returns>
        public static List<T> Rerank<T>(IReadOnlyList<T> items, Func<T, double> bonus)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(bonus);
            var count = Math.Max(1, items.Count);
            return items
                .Select((item, index) => (Item: item, Index: index, Score: (1.0 - ((double)index / count)) + bonus(item)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Index)
                .Select(x => x.Item)
                .ToList();
        }
    }
}
