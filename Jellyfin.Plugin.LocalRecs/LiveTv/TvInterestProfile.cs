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
        private const float RegularBonus = 0.5f;

        private readonly Dictionary<string, float> _genres = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _titles = new Dictionary<string, float>();
        private readonly HashSet<string> _regularTitles = new HashSet<string>();
        private readonly Dictionary<string, float> _people = new Dictionary<string, float>();
        private bool _normalized;

        /// <summary>
        /// Gets a value indicating whether the profile contains any interest.
        /// </summary>
        public bool IsEmpty => _genres.Count == 0 && _titles.Count == 0 && _people.Count == 0;

        /// <summary>
        /// Gets a value indicating whether the user has regular shows.
        /// </summary>
        public bool HasRegularShows => _regularTitles.Count > 0;

        /// <summary>
        /// Adds an interest signal.
        /// </summary>
        /// <param name="genres">Genres.</param>
        /// <param name="titleKey">Normalized title key (see <see cref="LiveTvText.NormalizeTitle"/>).</param>
        /// <param name="people">People.</param>
        /// <param name="weight">Weight of the signal.</param>
        /// <param name="sport">Detected sport (see <see cref="LiveTvText.DetectSport"/>), if any.</param>
        public void Add(IEnumerable<string>? genres, string? titleKey, IEnumerable<string>? people, float weight, string? sport = null)
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

            if (!string.IsNullOrEmpty(sport))
            {
                var sportKey = SportKey(sport);
                _titles[sportKey] = _titles.GetValueOrDefault(sportKey) + weight;
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
        /// Checks whether the user has an interest in exactly this title (e.g. "fußball").
        /// </summary>
        /// <param name="titleKey">Normalized title key.</param>
        /// <returns>True if the title was watched before.</returns>
        public bool HasTitle(string? titleKey)
        {
            return !string.IsNullOrEmpty(titleKey) && _titles.ContainsKey(titleKey);
        }

        /// <summary>
        /// Checks whether a title is one of the user's regular shows.
        /// </summary>
        /// <param name="titleKey">Normalized title key.</param>
        /// <returns>True for regular shows.</returns>
        public bool IsRegular(string? titleKey)
        {
            return !string.IsNullOrEmpty(titleKey) && _regularTitles.Contains(titleKey);
        }

        /// <summary>
        /// Marks a title as a regular show the user watches again and again (e.g. "die sendung mit der maus").
        /// Regular shows get a bonus and are always shown.
        /// </summary>
        /// <param name="titleKey">Normalized title key.</param>
        public void MarkRegular(string? titleKey)
        {
            if (!string.IsNullOrEmpty(titleKey))
            {
                _regularTitles.Add(titleKey);
            }
        }

        /// <summary>
        /// Checks whether the user has watched this kind of sport (e.g. "fußball").
        /// </summary>
        /// <param name="sport">Sport.</param>
        /// <returns>True if the sport was watched before.</returns>
        public bool HasSport(string? sport)
        {
            return !string.IsNullOrEmpty(sport) && _titles.ContainsKey(SportKey(sport));
        }

        /// <summary>
        /// Scores a program against the profile.
        /// </summary>
        /// <param name="genres">Genres of the program.</param>
        /// <param name="titleKey">Normalized title key of the program.</param>
        /// <param name="people">People of the program.</param>
        /// <param name="sport">Detected sport of the program, if any.</param>
        /// <returns>Score (0 = no match).</returns>
        public float Score(IEnumerable<string>? genres, string? titleKey, IEnumerable<string>? people, string? sport = null)
        {
            if (!_normalized)
            {
                Normalize();
            }

            var genreList = (genres ?? Enumerable.Empty<string>()).Select(LiveTvText.NormalizeFeature).Where(g => g.Length > 0).Distinct().ToList();
            var genreScore = genreList.Count == 0
                ? 0f
                : genreList.Sum(g => _genres.GetValueOrDefault(g)) / (float)Math.Sqrt(genreList.Count);

            var titleScore = Math.Max(
                string.IsNullOrEmpty(titleKey) ? 0f : _titles.GetValueOrDefault(titleKey),
                string.IsNullOrEmpty(sport) ? 0f : _titles.GetValueOrDefault(SportKey(sport)));

            var peopleScore = (people ?? Enumerable.Empty<string>())
                .Select(LiveTvText.NormalizeFeature)
                .Where(p => p.Length > 0)
                .Distinct()
                .Sum(p => _people.GetValueOrDefault(p));

            return (IsRegular(titleKey) ? RegularBonus : 0f)
                + (GenreFactor * Math.Min(genreScore, 1f))
                + (TitleFactor * titleScore)
                + (PeopleFactor * Math.Min(peopleScore, 1f));
        }

        private static string SportKey(string sport) => "sport:" + sport;

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
