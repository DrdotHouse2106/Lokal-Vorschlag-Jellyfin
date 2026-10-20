using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Diagnose: Jellyfin Web meldet Fehler und die letzten Schritte vor einem Absturz (z. B. in der iOS-App) ins Serverlog.
    /// Nur wenn „Diagnose“ in den Einstellungen an ist. Zugangsschlüssel werden vor dem Schreiben entfernt.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/ClientLog")]
    public class ClientLogController : ControllerBase
    {
        /// <summary>Maximum number of lines per request.</summary>
        public const int MaxLines = 60;

        /// <summary>Maximum length of a line.</summary>
        public const int MaxLineLength = 800;

        private const int MaxLinesPerMinute = 300;

        private static readonly ConcurrentDictionary<Guid, (long Minute, int Count)> Budget = new ConcurrentDictionary<Guid, (long Minute, int Count)>();

        private static readonly Regex SecretPattern = new Regex(
            @"((?:api_?key|access_?token|token|x-emby-token|x-mediabrowser-token)\s*[=:]\s*""?)[^&""\s,;]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(200));

        private readonly IUserManager _userManager;
        private readonly ILogger<ClientLogController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientLogController"/> class.
        /// </summary>
        /// <param name="userManager">User manager.</param>
        /// <param name="logger">Logger.</param>
        public ClientLogController(IUserManager userManager, ILogger<ClientLogController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Removes access keys and control characters and shortens the line.
        /// </summary>
        /// <param name="line">Line from the client.</param>
        /// <returns>Line safe for the server log.</returns>
        public static string Sanitize(string? line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            var text = line.Length > MaxLineLength * 2 ? line.Substring(0, MaxLineLength * 2) : line;
            try
            {
                text = SecretPattern.Replace(text, "$1***");
            }
            catch (RegexMatchTimeoutException)
            {
                return "(Zeile verworfen)";
            }

            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
            return text.Length > MaxLineLength ? text.Substring(0, MaxLineLength) + "…" : text;
        }

        /// <summary>
        /// Writes lines of the web module to the server log.
        /// </summary>
        /// <param name="request">Lines.</param>
        /// <returns>No content.</returns>
        [HttpPost]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Post([FromBody] ClientLogRequest request)
        {
            if (!Config.WebDiagnostics || request?.Lines == null)
            {
                return NoContent();
            }

            var userId = RecommendationsController.GetUserId(User);
            var minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            var lines = request.Lines.Take(MaxLines).ToList();
            var budget = Budget.AddOrUpdate(
                userId,
                _ => (minute, lines.Count),
                (_, old) => old.Minute == minute ? (minute, old.Count + lines.Count) : (minute, lines.Count));
            if (budget.Count > MaxLinesPerMinute)
            {
                return NoContent();
            }

            var userName = userId == Guid.Empty ? "?" : _userManager.GetUserById(userId)?.Username ?? "?";
            var client = Claim("Jellyfin-Client") + " " + Claim("Jellyfin-Version") + " · " + Claim("Jellyfin-Device");
            foreach (var line in lines)
            {
                _logger.LogWarning("Quallenkino-Diagnose [{User} · {Client}] {Line}", userName, Sanitize(client), Sanitize(line));
            }

            return NoContent();
        }

        private string Claim(string type)
        {
            return User.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value ?? "?";
        }

        /// <summary>
        /// Lines of the web module.
        /// </summary>
        public class ClientLogRequest
        {
            /// <summary>Gets or sets the lines (at most 60 per request are written).</summary>
            public IReadOnlyList<string>? Lines { get; set; }
        }
    }
}
