using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Configuration;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Einstellungen der Regel-Bausteine (jeder einzeln schaltbar).
    /// </summary>
    public class EpgRuleSettings
    {
        /// <summary>Gets or sets a value indicating whether an episode number makes a series episode.</summary>
        public bool SeriesByEpisodeNumber { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether a sub-title (episode title) makes a series episode if it is no movie.</summary>
        public bool SeriesBySubTitle { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether the movie rule is on.</summary>
        public bool MovieRule { get; set; } = true;

        /// <summary>Gets or sets the minimum length of a movie in minutes.</summary>
        public int MovieMinMinutes { get; set; } = 75;

        /// <summary>Gets or sets a value indicating whether a director counts as movie evidence.</summary>
        public bool MovieByDirector { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether a &lt;date&gt; counts as movie evidence.</summary>
        public bool MovieByDate { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether an IMDb link counts as movie evidence.</summary>
        public bool MovieByImdb { get; set; } = true;

        /// <summary>Gets or sets categories that count as movie evidence (e.g. "Spielfilm"), only together with the minimum length.</summary>
        public IReadOnlyList<string> MovieCategories { get; set; } = new[] { "Spielfilm" };

        /// <summary>Gets or sets the kids categories.</summary>
        public IReadOnlyList<string> KidsCategories { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the kids channels (name contains).</summary>
        public IReadOnlyList<string> KidsChannels { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the news categories.</summary>
        public IReadOnlyList<string> NewsCategories { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the news channels.</summary>
        public IReadOnlyList<string> NewsChannels { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the sports categories.</summary>
        public IReadOnlyList<string> SportsCategories { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the sports channels.</summary>
        public IReadOnlyList<string> SportsChannels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Creates the settings from the plugin configuration.
        /// </summary>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>The settings.</returns>
        public static EpgRuleSettings From(PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new EpgRuleSettings
            {
                SeriesByEpisodeNumber = config.EpgSeriesByEpisodeNumber,
                SeriesBySubTitle = config.EpgSeriesBySubTitle,
                MovieRule = config.EpgMovieRule,
                MovieMinMinutes = Math.Clamp(config.EpgMovieMinMinutes, 1, 600),
                MovieByDirector = config.EpgMovieByDirector,
                MovieByDate = config.EpgMovieByDate,
                MovieByImdb = config.EpgMovieByImdb,
                MovieCategories = SplitList(config.EpgMovieCategories),
                KidsCategories = SplitList(config.EpgKidsCategories),
                KidsChannels = SplitList(config.EpgKidsChannels),
                NewsCategories = SplitList(config.EpgNewsCategories),
                NewsChannels = SplitList(config.EpgNewsChannels),
                SportsCategories = SplitList(config.EpgSportsCategories),
                SportsChannels = SplitList(config.EpgSportsChannels)
            };
        }

        /// <summary>
        /// Splits a list separated by commas, semicolons or lines.
        /// </summary>
        /// <param name="value">The list.</param>
        /// <returns>The entries, trimmed, without empty ones.</returns>
        public static IReadOnlyList<string> SplitList(string? value) =>
            (value ?? string.Empty).Split(new[] { ',', ';', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
