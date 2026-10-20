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
                TraktPersonal = Config.EnableTraktPersonal && Config.TraktPersonalWeight > 0,
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

            /// <summary>Gets or sets a value indicating whether personal Trakt recommendations are available (since 12.33.0).</summary>
            public bool TraktPersonal { get; set; }

            /// <summary>Gets or sets the plugin version.</summary>
            public string Version { get; set; } = string.Empty;
        }
    }
}
