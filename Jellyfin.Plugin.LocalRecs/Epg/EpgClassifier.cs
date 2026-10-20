using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Regel-Bausteine: entscheiden Serie oder Film aus den Merkmalen der Sendung statt aus einer Kategorienliste.
    /// Reihenfolge: Serie über Folgennummer → Film (Länge + Regie/date/IMDb/Kategorie) → Serie über Folgentitel.
    /// Kinder, Nachrichten und Sport über Kategorie oder Sender. Die Kategorien bleiben als Genres erhalten.
    /// </summary>
    public static class EpgClassifier
    {
        private static readonly Regex ImdbId = new Regex(@"imdb\.[a-z.]+/title/(tt\d{5,10})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Year = new Regex(@"^(1[89]\d\d|20\d\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Classifies a programme.
        /// </summary>
        /// <param name="programme">The programme.</param>
        /// <param name="channelName">Display name of the channel (for channel rules).</param>
        /// <param name="settings">Rule settings.</param>
        /// <param name="extra">Movie hints from the second XMLTV file, optional.</param>
        /// <returns>The classification with reasons.</returns>
        public static EpgClassification Classify(EpgProgramme programme, string? channelName, EpgRuleSettings settings, EpgExtra? extra = null)
        {
            ArgumentNullException.ThrowIfNull(programme);
            ArgumentNullException.ThrowIfNull(settings);
            var result = new EpgClassification();
            var year = programme.Date == null ? null : Year.Match(programme.Date);
            result.Year = year != null && year.Success ? int.Parse(year.Value, CultureInfo.InvariantCulture) : null;
            result.ImdbId = programme.Urls.Select(u => ImdbId.Match(u)).Where(m => m.Success).Select(m => m.Groups[1].Value).FirstOrDefault();
            result.Year ??= extra?.Year;
            result.ImdbId ??= string.IsNullOrWhiteSpace(extra?.ImdbId) ? null : extra.ImdbId;

            // Kinder, Nachrichten, Sport: Kategorie oder Sender
            result.IsKids = Flag(programme, channelName, settings.KidsCategories, settings.KidsChannels, "Kinder", result.Reasons);
            result.IsNews = Flag(programme, channelName, settings.NewsCategories, settings.NewsChannels, "Nachrichten", result.Reasons);
            result.IsSports = Flag(programme, channelName, settings.SportsCategories, settings.SportsChannels, "Sport", result.Reasons);

            // 0. Nie Film oder Serie (z. B. Teleshopping), über Kategorie oder Sender
            var otherCategory = programme.Categories.FirstOrDefault(c => Matches(c, settings.OtherCategories));
            var otherChannel = string.IsNullOrEmpty(channelName) ? null : settings.OtherChannels.FirstOrDefault(c => channelName.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (otherCategory != null || otherChannel != null)
            {
                result.Reasons.Add("Sonstiges: " + (otherCategory != null ? "Kategorie „" + otherCategory + "“" : "Sender „" + channelName + "“"));
                return result;
            }

            // 1. Serie über Folgennummer (xmltv_ns oder onscreen)
            var (season, episode) = EpgFile.ParseXmltvNs(programme.EpisodeNumXmltvNs);
            if (season == null && episode == null)
            {
                (season, episode) = EpgFile.ParseOnscreen(programme.EpisodeNumOnscreen);
            }

            if (settings.SeriesByEpisodeNumber && (season != null || episode != null))
            {
                result.IsSeries = true;
                result.Season = season;
                result.Episode = episode;
                result.EpisodeTitle = programme.SubTitle;
                result.Reasons.Add("Serie: Folgennummer " + (season != null ? "S" + season.Value.ToString("00", CultureInfo.InvariantCulture) : string.Empty)
                    + (episode != null ? "E" + episode.Value.ToString("00", CultureInfo.InvariantCulture) : string.Empty));
                return result;
            }

            // 2. Film: lang genug und ein Film-Merkmal – nicht bei Sport und Nachrichten
            var noMovie = programme.Categories.FirstOrDefault(c => Matches(c, settings.NoMovieCategories));
            if (settings.MovieRule && noMovie != null)
            {
                result.Reasons.Add("kein Film: Kategorie „" + noMovie + "“");
            }

            if (settings.MovieRule && !result.IsSports && !result.IsNews && noMovie == null)
            {
                var evidence = MovieEvidence(programme, result, settings, extra);
                if (programme.Minutes >= settings.MovieMinMinutes && evidence != null)
                {
                    result.IsMovie = true;
                    result.Reasons.Add("Film: " + Math.Round(programme.Minutes).ToString(CultureInfo.InvariantCulture) + " Min. und " + evidence);
                    return result;
                }

                if (evidence != null)
                {
                    result.Reasons.Add("kein Film: nur " + Math.Round(programme.Minutes).ToString(CultureInfo.InvariantCulture) + " Min. (" + evidence + ")");
                }
                else if (programme.Minutes >= settings.MovieMinMinutes)
                {
                    result.Reasons.Add("kein Film: lang, aber ohne Regie, IMDb oder Film-Kategorie");
                }
            }

            // 3. Serie über Folgentitel – nicht bei Sport („Champions League: Highlights“ ist keine Serienfolge)
            if (settings.SeriesBySubTitle && !result.IsSports && !string.IsNullOrWhiteSpace(programme.SubTitle))
            {
                result.IsSeries = true;
                result.EpisodeTitle = programme.SubTitle;
                result.Reasons.Add("Serie: Folgentitel „" + programme.SubTitle + "“");
            }

            return result;
        }

        private static string? MovieEvidence(EpgProgramme programme, EpgClassification result, EpgRuleSettings settings, EpgExtra? extra)
        {
            if (settings.MovieByDirector && programme.Directors.Count > 0)
            {
                return "Regie (" + programme.Directors[0] + ")";
            }

            if (settings.MovieByDirector && extra != null && extra.Directors.Count > 0)
            {
                return "Regie (" + extra.Directors[0] + ", aus der Darsteller-Quelle)";
            }

            if (settings.MovieByImdb && result.ImdbId != null)
            {
                return "IMDb " + result.ImdbId;
            }

            var category = programme.Categories.FirstOrDefault(c => Matches(c, settings.MovieCategories));
            if (category != null)
            {
                return "Kategorie „" + category + "“";
            }

            // Jahr allein ist schwach (Shows und Teleshopping haben oft eins) – nur wenn ausdrücklich eingeschaltet
            if (settings.MovieByYear && result.Year != null)
            {
                return "Jahr " + result.Year.Value.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static bool Flag(EpgProgramme programme, string? channelName, IReadOnlyList<string> categories, IReadOnlyList<string> channels, string label, List<string> reasons)
        {
            var category = programme.Categories.FirstOrDefault(c => Matches(c, categories));
            if (category != null)
            {
                reasons.Add(label + ": Kategorie „" + category + "“");
                return true;
            }

            var channel = string.IsNullOrEmpty(channelName) ? null : channels.FirstOrDefault(c => channelName.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                reasons.Add(label + ": Sender „" + channelName + "“");
                return true;
            }

            return false;
        }

        // Ganze Wörter vergleichen: „Sport“ trifft „Sport“ und „Sport live“, aber nicht „Motorsport“; „Fußball“ trifft „Fußball International“
        private static bool Matches(string category, IReadOnlyList<string> keywords) =>
            keywords.Any(k => string.Equals(category, k, StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(category, @"(^|[\s/,&(-])" + Regex.Escape(k) + @"($|[\s/,&)-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
