using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Discover;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Entdecken-Reihen aus TMDB für Jellyfin Web und die Apps: „Demnächst im Kino“, „Neu dieses Jahr – Filme“ und
    /// „Neu dieses Jahr – Serien“. Nur lesend, es wird nichts in Jellyseerr angefragt.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Discover")]
    [Authorize]
    public class DiscoverController : ControllerBase
    {
        private readonly DiscoverService _discover;
        private readonly IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoverController"/> class.
        /// </summary>
        /// <param name="discover">Discover rows.</param>
        /// <param name="userManager">User manager.</param>
        public DiscoverController(DiscoverService discover, IUserManager userManager)
        {
            _discover = discover ?? throw new ArgumentNullException(nameof(discover));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        /// <summary>
        /// Gets the discover rows that are not empty, in display order. Keys: <c>cinema</c>, <c>newmovies</c>, <c>newseries</c>.
        /// Each title carries <c>InLibrary</c>/<c>JellyfinId</c> for the signed-in user; titles above the user's parental rating are left out.
        /// </summary>
        /// <param name="key">Only this row, optional.</param>
        /// <returns>The rows.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DiscoverResult>> Get([FromQuery] string? key = null)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            Response.Headers.CacheControl = "no-store";
            var rows = await _discover.GetAsync(user, HttpContext.RequestAborted, string.IsNullOrWhiteSpace(key) ? null : key.Trim()).ConfigureAwait(false);
            return new DiscoverResult { Rows = rows.ToList() };
        }

        /// <summary>The discover rows.</summary>
        public class DiscoverResult
        {
            /// <summary>Gets or sets the rows (only non-empty ones).</summary>
            public IReadOnlyList<DiscoverService.Row> Rows { get; set; } = Array.Empty<DiscoverService.Row>();

            /// <summary>Gets the number of rows.</summary>
            public int TotalRecordCount => Rows.Count;
        }
    }
}
