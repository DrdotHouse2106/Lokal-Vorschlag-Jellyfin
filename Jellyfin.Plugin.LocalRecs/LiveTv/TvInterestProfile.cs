using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Einfaches, nachvollziehbares Interessenprofil eines Benutzers für das Live-TV:
    /// Gewichte für Genres, Titel (Sendungen/Serien) und Personen.
    /// Live geschaute Minuten und der Sehverlauf aus der Bibliothek fließen gemeinsam ein.
    /// Wer bei der Fußball-Konferenz zwischen den Sendern wechselt, sammelt so die Minuten aller Spiele
    /// gemeinsam beim Genre und Titel „Fußball“.
    /// </summary>
    public class TvInterestProfile
    {
        private const float GenreFactor = 0.45f;
        private const float TitleFactor = 1.0f;
        private const float PeopleFactor = 0.3f;

        private readonly Dictionary<string, float> _genres = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _titles = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _people = new Dictionary<string, float>();
        private bool _normalized;

        /// <summary>
        /// Gets a value indicating whether the profile contains any interest.
        /// </summary>
        public bool IsEmpty => _genres.Count == 0 && _titles.Count == 0 && _people.Count == 0;

        /// <summary>
        /// Adds an interest signal.
        /// </summary>
        /// <param name="genres">Genres.</param>
        /// <param name="titleKey">Normalized title key (see <see cref="LiveTvText.NormalizeTitle"/>).</param>
        /// <param name="people">People.</param>
        /// <param name="weight">Weight of the signal.</param>
        public void Add(IEnumerable<string>? genres, string? titleKey, IEnumerable<string>? people, float weight)
        {
            if (weight <= 0)
            {
                return;
            }

            if (_normalized)
            {
                throw new InvalidOperationException("Profile is already normalized");
            }

            foreach (var genre in (genres ?? Enumerable.Empty<string>()).Select(LiveTvText.NormalizeFeature).Where(g => g.Length > 0).Distinct())
            {
                _genres[genre] = _genres.GetValueOrDefault(genre) + weight;
            }

            if (!string.IsNullOrEmpty(titleKey))
            {
                _titles[titleKey] = _titles.GetValueOrDefault(titleKey) + weight;
            }

            foreach (var person in (people ?? Enumerable.Empty<string>()).Select(LiveTvText.NormalizeFeature).Where(p => p.Length > 0).Distinct())
            {
                _people[person] = _people.GetValueOrDefault(person) + weight;
            }
        }

        /// <summary>
        /// Scales all weights to the range 0..1. Must be called once after adding all signals.
        /// </summary>
        public void Normalize()
        {
            ScaleToMax(_genres);
            ScaleToMax(_titles);
            ScaleToMax(_people);
            _normalized = true;
        }

        /// <summary>
        /// Scores a program against the profile.
        /// </summary>
        /// <param name="genres">Genres of the program.</param>
        /// <param name="titleKey">Normalized title key of the program.</param>
        /// <param name="people">People of the program.</param>
        /// <returns>Score (0 = no match).</returns>
        public float Score(IEnumerable<string>? genres, string? titleKey, IEnumerable<string>? people)
        {
            if (!_normalized)
            {
                Normalize();
            }

            var genreList = (genres ?? Enumerable.Empty<string>()).Select(LiveTvText.NormalizeFeature).Where(g => g.Length > 0).Distinct().ToList();
            var genreScore = genreList.Count == 0
                ? 0f
                : genreList.Sum(g => _genres.GetValueOrDefault(g)) / (float)Math.Sqrt(genreList.Count);

            var titleScore = string.IsNullOrEmpty(titleKey) ? 0f : _titles.GetValueOrDefault(titleKey);

            var peopleScore = (people ?? Enumerable.Empty<string>())
                .Select(LiveTvText.NormalizeFeature)
                .Where(p => p.Length > 0)
                .Distinct()
                .Sum(p => _people.GetValueOrDefault(p));

            return (GenreFactor * Math.Min(genreScore, 1f))
                + (TitleFactor * titleScore)
                + (PeopleFactor * Math.Min(peopleScore, 1f));
        }

        private static void ScaleToMax(Dictionary<string, float> weights)
        {
            if (weights.Count == 0)
            {
                return;
            }

            var max = weights.Values.Max();
            if (max <= 0)
            {
                return;
            }

            foreach (var key in weights.Keys.ToList())
            {
                weights[key] /= max;
            }
        }
    }
}
