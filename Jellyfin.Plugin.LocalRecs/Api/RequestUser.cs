using System;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Für wen eine Anfrage an <c>/LocalRecs</c> gilt: den angemeldeten Benutzer des Tokens. Mit einem Server-API-Schlüssel
    /// (ohne Benutzer) oder als Administrator gilt stattdessen der Parameter <c>userId</c>. Andere Benutzer können ihn nicht
    /// nutzen, um fremde Daten zu sehen – für sie zählt immer das eigene Profil.
    /// </summary>
    public static class RequestUser
    {
        private const string ApiKeyClaim = "Jellyfin-IsApiKey";

        /// <summary>
        /// Gets the user a request is for, or null if there is none (API key without <c>userId</c>, unknown user).
        /// </summary>
        /// <param name="controller">The controller.</param>
        /// <param name="userManager">User manager.</param>
        /// <returns>The user, or null.</returns>
        public static User? Resolve(ControllerBase controller, IUserManager userManager)
        {
            ArgumentNullException.ThrowIfNull(controller);
            ArgumentNullException.ThrowIfNull(userManager);
            var tokenId = RecommendationsController.GetUserId(controller.User);
            var requested = Guid.Empty;
            var raw = controller.HttpContext?.Request.Query["userId"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw.Trim(), out var parsed))
            {
                requested = parsed;
            }

            if (tokenId != Guid.Empty)
            {
                var own = userManager.GetUserById(tokenId);
                if (own == null || requested == Guid.Empty || requested == tokenId)
                {
                    return own;
                }

                return own.HasPermission(PermissionKind.IsAdministrator) ? userManager.GetUserById(requested) : own;
            }

            var apiKey = controller.User?.Claims.Any(c => string.Equals(c.Type, ApiKeyClaim, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)) == true;
            return apiKey && requested != Guid.Empty ? userManager.GetUserById(requested) : null;
        }

        /// <summary>
        /// Gets the answer when no user is known: 400 with a hint instead of an exception.
        /// </summary>
        /// <param name="controller">The controller.</param>
        /// <returns>The answer.</returns>
        public static ActionResult Missing(ControllerBase controller)
        {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.BadRequest("Kein Benutzer: Mit einem Server-API-Schlüssel bitte ?userId=<Benutzer-ID> angeben (oder mit dem Token eines Benutzers anfragen).");
        }
    }
}
