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
        /// Checks whether a user gets the Netflix-style player (IDs compared without dashes, case-insensitive).
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
                PlayerEnabled = IsPlayerUser(Config.WebPlayerUserIds, userId),
                Version = WebIntegration.Version
            };
        }

        /// <summary>
        /// Settings of the web module.
        /// </summary>
        public class WebConfig
        {
            /// <summary>Gets or sets a value indicating whether the Netflix-style start page is on.</summary>
            public bool HomeEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the Quallenkino colors are on.</summary>
            public bool ThemeEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the signed-in user gets the Netflix-style player.</summary>
            public bool PlayerEnabled { get; set; }

            /// <summary>Gets or sets the plugin version.</summary>
            public string Version { get; set; } = string.Empty;
        }
    }
}
