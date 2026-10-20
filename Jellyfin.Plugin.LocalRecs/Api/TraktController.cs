using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Persönliche Trakt-Empfehlungen des angemeldeten Benutzers: Stand, ein-/ausschalten, eigene Verbindung per Code.
    /// Jeder nur für sich selbst; Zugangsdaten verlassen den Server nie in Richtung Browser.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Trakt/Me")]
    [Authorize]
    public class TraktController : ControllerBase
    {
        private readonly TraktPersonalService _trakt;

        /// <summary>
        /// Initializes a new instance of the <see cref="TraktController"/> class.
        /// </summary>
        /// <param name="trakt">Trakt service.</param>
        public TraktController(TraktPersonalService trakt)
        {
            _trakt = trakt ?? throw new ArgumentNullException(nameof(trakt));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the state for the signed-in user.
        /// </summary>
        /// <returns>The state.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<TraktStatus> Get()
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            Response.Headers.CacheControl = "no-store";
            return _trakt.GetStatus(userId, Config);
        }

        /// <summary>
        /// Switches the personal recommendations on or off for the signed-in user.
        /// </summary>
        /// <param name="request">On or off.</param>
        /// <returns>The new state.</returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<TraktStatus> Set([FromBody] EnabledRequest request)
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty || request == null)
            {
                return Unauthorized();
            }

            _trakt.SetEnabled(userId, request.Enabled);
            return _trakt.GetStatus(userId, Config);
        }

        /// <summary>
        /// Starts the own connection with a code (needs an own Trakt app in the settings).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The state with code and address.</returns>
        [HttpPost("Login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<TraktStatus>> Login(CancellationToken cancellationToken)
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            return await _trakt.StartLoginAsync(userId, Config, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the own connection (the link of the Trakt plugin stays untouched).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The new state.</returns>
        [HttpDelete("Login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<TraktStatus>> Logout(CancellationToken cancellationToken)
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            await _trakt.DisconnectAsync(userId, Config, cancellationToken).ConfigureAwait(false);
            return _trakt.GetStatus(userId, Config);
        }

        /// <summary>
        /// On or off.
        /// </summary>
        public class EnabledRequest
        {
            /// <summary>Gets or sets a value indicating whether the personal recommendations are used.</summary>
            public bool Enabled { get; set; }
        }
    }
}
