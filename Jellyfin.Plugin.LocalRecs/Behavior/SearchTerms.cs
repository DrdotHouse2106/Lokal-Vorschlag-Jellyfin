using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Prüft Suchbegriffe, bevor sie als Interesse zählen: Füllwörter („the“, „der“, „und“ …) und Bruchstücke unter drei
    /// Zeichen sind keine Suche. Und ein Grund „Weil du nach … gesucht hast“ passt nur, wenn der Titel den Begriff enthält.
    /// </summary>
    public static class SearchTerms
    {
        /// <summary>Shortest meaningful word.</summary>
        public const int MinWordLength = 3;

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "the", "a", "an", "of", "and", "or", "to", "in", "on", "at", "for", "with", "from", "by", "is", "it",
            "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "eines", "und", "oder",
            "von", "vom", "zu", "zum", "zur", "im", "am", "an", "auf", "mit", "fur", "aus", "bei", "ist",
            "le", "la", "les", "el", "los", "las", "il", "de", "du", "et"
        };

        /// <summary>
        /// Splits a text into lower-case words without accents.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The words.</returns>
        public static IReadOnlyList<string> Words(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            var builder = new StringBuilder(text.Length);
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            return builder.ToString().Replace("ß", "ss", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Gets the words of a search term that carry meaning (no stop words).
        /// </summary>
        /// <param name="term">The search term.</param>
        /// <returns>The words.</returns>
        public static IReadOnlyList<string> MeaningfulWords(string? term) => Words(term).Where(w => !StopWords.Contains(w)).ToList();

        /// <summary>
        /// Gets whether a search term is a real search: at least one word of three or more characters that is not a stop word
        /// („The“, „de“, „x“ are not).
        /// </summary>
        /// <param name="term">The search term.</param>
        /// <returns>True if it counts.</returns>
        public static bool IsMeaningful(string? term) => MeaningfulWords(term).Any(w => w.Length >= MinWordLength);

        /// <summary>
        /// Gets whether a title really fits a search term: every meaningful word of the term starts a word of the title
        /// („dead“ fits „Deadpool“, „the“ alone fits nothing).
        /// </summary>
        /// <param name="term">The search term.</param>
        /// <param name="title">The title.</param>
        /// <returns>True if it fits.</returns>
        public static bool TitleFits(string? term, string? title)
        {
            var wanted = MeaningfulWords(term);
            if (wanted.Count == 0)
            {
                return false;
            }

            var words = Words(title);
            var joined = string.Concat(words);
            return wanted.All(w => words.Any(t => t.StartsWith(w, StringComparison.Ordinal)) || (w.Length >= 4 && joined.Contains(w, StringComparison.Ordinal)));
        }

        /// <summary>
        /// Gets whether one term is an intermediate state of another while typing (one starts with the other).
        /// </summary>
        /// <param name="a">One term.</param>
        /// <param name="b">The other term.</param>
        /// <returns>True if one is a prefix of the other.</returns>
        public static bool IsTypingStep(string a, string b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            var x = a.Trim();
            var y = b.Trim();
            return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase);
        }
    }
}
