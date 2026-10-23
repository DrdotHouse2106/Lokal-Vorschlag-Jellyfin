using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Liefert das Web-Modul für Jellyfin Web aus (Skript und Stylesheet) und dessen Einstellungen.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Web")]
    public class WebController : ControllerBase
    {
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["localrecs-web.js"] = "application/javascript; charset=utf-8",
            ["localrecs-web.css"] = "text/css; charset=utf-8"
        };

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Checks whether a user gets the Quallenkino-style player (IDs compared without dashes, case-insensitive).
        /// </summary>
        /// <param name="userIds">Configured user IDs.</param>
        /// <param name="userId">User ID.</param>
        /// <returns>True if the user is in the list.</returns>
        public static bool IsPlayerUser(IEnumerable<string>? userIds, string? userId)
        {
            if (userIds == null || string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            var normalized = userId.Replace("-", string.Empty, StringComparison.Ordinal);
            return userIds.Any(id => string.Equals(id?.Replace("-", string.Empty, StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a file of the web module. Without sign-in, because index.html loads it before the login.
        /// </summary>
        /// <param name="fileName">"localrecs-web.js" or "localrecs-web.css".</param>
        /// <returns>The file.</returns>
        [HttpGet("{fileName}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult GetFile([FromRoute] string fileName)
        {
            if (!Files.TryGetValue(fileName, out var contentType))
            {
                return NotFound();
            }

            var stream = typeof(WebController).Assembly.GetManifestResourceStream(typeof(WebIntegration).Namespace + "." + fileName);
            if (stream == null)
            {
                return NotFound();
            }

            // Mit Versionsangabe im Link (?v=…) eingebunden – neue Versionen kommen über einen neuen Link
            Response.Headers.CacheControl = "public, max-age=86400";
            return File(stream, contentType);
        }

        /// <summary>
        /// Gets the settings of the web module.
        /// </summary>
        /// <returns>Whether the start page and the colors are switched on.</returns>
        [HttpGet("Config")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<WebConfig> GetConfig()
        {
            Response.Headers.CacheControl = "no-store";
            var userId = RecommendationsController.GetUserId(User).ToString("N");
            return new WebConfig
            {
                HomeEnabled = Config.WebHomeEnabled,
                ThemeEnabled = Config.WebThemeEnabled,
                PlayerEnabled = Config.WebPlayerForAll || IsPlayerUser(Config.WebPlayerUserIds, userId),
                ProfilesEnabled = Config.WebProfilesEnabled,
                Avatars = Config.AvatarsEnabled,
                AvatarCatalogUrl = Avatars.AvatarService.ConfiguredCatalog(),
                Reminders = true,
                Diagnostics = Config.WebDiagnostics,
                BehaviorSignals = Config.EnableBehaviorSignals,
                TraktPersonal = Config.EnableTraktPersonal && Config.TraktPersonalWeight > 0,
                Billboard = true,
                MovieSeries = Config.EnableMovieSeriesSection,
                Seasonal = Config.EnableSeasonal,
                Moments = Moments.MomentsService.Keys.Any(k => Moments.MomentsService.IsEnabled(k, Config)),
                Upcoming = Config.EnableUpcoming && (Upcoming.ArrClient.BaseUrl(Config.SonarrUrl) != null || Upcoming.ArrClient.BaseUrl(Config.RadarrUrl) != null),
                MovieSeriesTitle = string.IsNullOrWhiteSpace(Config.MovieSeriesSectionTitle) ? "Filmreihen für dich" : Config.MovieSeriesSectionTitle,
                MoviesTitle = string.IsNullOrWhiteSpace(Config.MovieSectionTitle) ? "Empfohlene Filme für dich" : Config.MovieSectionTitle,
                SeriesTitle = string.IsNullOrWhiteSpace(Config.TvSectionTitle) ? "Empfohlene Serien für dich" : Config.TvSectionTitle,
                LiveTvTitle = string.IsNullOrWhiteSpace(Config.LiveTvSectionTitle) ? "Heute im TV für dich" : Config.LiveTvSectionTitle,
                LiveTvRow = Config.EnableLiveTvSection,
                Version = WebIntegration.Version
            };
        }

        /// <summary>
        /// Settings of the web module.
        /// </summary>
        public class WebConfig
        {
            /// <summary>Gets or sets a value indicating whether the Quallenkino-style start page is on.</summary>
            public bool HomeEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the Quallenkino colors are on.</summary>
            public bool ThemeEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the signed-in user gets the Quallenkino-style player.</summary>
            public bool PlayerEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the "Wer schaut?" profile picker is on (since 12.21.0).</summary>
            public bool ProfilesEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether users can choose a profile image from icons (since 12.22.0).</summary>
            public bool Avatars { get; set; }

            /// <summary>Gets or sets the configured catalog of the profile images (for information; apps use /LocalRecs/Avatars).</summary>
            public string AvatarCatalogUrl { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether live TV reminders and the calendar feed are available (since 12.20.0).</summary>
            public bool Reminders { get; set; }

            /// <summary>Gets or sets a value indicating whether the web module reports errors to /LocalRecs/ClientLog (since 12.30.0).</summary>
            public bool Diagnostics { get; set; }

            /// <summary>Gets or sets a value indicating whether searches and viewed titles are noted (since 12.36.0).</summary>
            public bool BehaviorSignals { get; set; }

            /// <summary>Gets or sets a value indicating whether personal Trakt recommendations are available (since 12.33.0).</summary>
            public bool TraktPersonal { get; set; }

            /// <summary>Gets or sets a value indicating whether the server offers the banner of the day (GET /LocalRecs/Billboard, since 12.42.0).</summary>
            public bool Billboard { get; set; }

            /// <summary>Gets or sets a value indicating whether „Filmreihen für dich“ is on (GET /LocalRecs/MovieSeries, since 12.45.0).</summary>
            public bool MovieSeries { get; set; }

            /// <summary>Gets or sets the title of the movie series row.</summary>
            public string MovieSeriesTitle { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether „Passend zur Jahreszeit“ is on (GET /LocalRecs/Seasonal, since 12.45.0).</summary>
            public bool Seasonal { get; set; }

            /// <summary>Gets or sets a value indicating whether at least one time-based row is on (GET /LocalRecs/Moments, since 12.46.0).</summary>
            public bool Moments { get; set; }

            /// <summary>Gets or sets a value indicating whether „Demnächst“ is on and Sonarr or Radarr is set up (GET /LocalRecs/Upcoming, since 12.48.0).</summary>
            public bool Upcoming { get; set; }

            /// <summary>Gets or sets the title of the movie recommendations (since 12.48.0).</summary>
            public string MoviesTitle { get; set; } = string.Empty;

            /// <summary>Gets or sets the title of the series recommendations (since 12.48.0).</summary>
            public string SeriesTitle { get; set; } = string.Empty;

            /// <summary>Gets or sets the title of „Heute im TV für dich“ (since 12.48.0).</summary>
            public string LiveTvTitle { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether „Heute im TV für dich“ is on (since 12.48.0).</summary>
            public bool LiveTvRow { get; set; }

            /// <summary>Gets or sets the plugin version.</summary>
            public string Version { get; set; } = string.Empty;
        }
    }
}
