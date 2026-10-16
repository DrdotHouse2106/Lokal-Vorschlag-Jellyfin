using System;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Empfehlungen des angemeldeten Benutzers für Apps, die „Home Screen Sections“ nicht anzeigen
    /// (z. B. die Android-TV-/Fire-TV-App „Jellyfin-TV-Client“).
    /// Liefert dieselben Einträge wie die Startseiten-Zeilen.
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    [Authorize]
    public class RecommendationsController : ControllerBase
    {
        /// <summary>
        /// Value of <c>type</c> for "Heute im TV für dich".
        /// </summary>
        public const string LiveTvType = "livetv";

        // Claim, in dem Jellyfin die ID des angemeldeten Benutzers ablegt
        private const string UserIdClaim = "Jellyfin-UserId";

        private readonly RecommendationSectionHandler _recommendations;
        private readonly LiveTvSectionHandler _liveTv;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationsController"/> class.
        /// </summary>
        /// <param name="recommendations">Handler for movie and series recommendations.</param>
        /// <param name="liveTv">Handler for live TV recommendations.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public RecommendationsController(RecommendationSectionHandler recommendations, LiveTvSectionHandler liveTv)
        {
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
            _liveTv = liveTv ?? throw new ArgumentNullException(nameof(liveTv));
        }

        /// <summary>
        /// Reads the user ID from the claims of the request.
        /// </summary>
        /// <param name="user">Claims of the request.</param>
        /// <returns>The user ID or <see cref="Guid.Empty"/>.</returns>
        public static Guid GetUserId(ClaimsPrincipal? user)
        {
            var value = user?.Claims.FirstOrDefault(c => string.Equals(c.Type, UserIdClaim, StringComparison.OrdinalIgnoreCase))?.Value;
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }

        /// <summary>
        /// Shortens the result to <paramref name="limit"/> items.
        /// </summary>
        /// <param name="result">Full result.</param>
        /// <param name="limit">Maximum number of items, null or less than 1 for all.</param>
        /// <returns>The shortened result.</returns>
        public static QueryResult<BaseItemDto> Limit(QueryResult<BaseItemDto> result, int? limit)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (limit is null or < 1 || result.Items.Count <= limit.Value)
            {
                return result;
            }

            var items = result.Items.Take(limit.Value).ToList();
            return new QueryResult<BaseItemDto>(null, result.TotalRecordCount, items);
        }

        /// <summary>
        /// Gets the recommendations of the signed-in user.
        /// </summary>
        /// <param name="type">"movies", "series" or "livetv".</param>
        /// <param name="limit">Optional maximum number of items.</param>
        /// <returns>The items in recommendation order.</returns>
        [HttpGet("Recommendations")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<QueryResult<BaseItemDto>> GetRecommendations([FromQuery] string type = RecommendationSectionHandler.MoviesSection, [FromQuery] int? limit = null)
        {
            var userId = GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            var request = new SectionRequest { UserId = userId, AdditionalData = type };
            QueryResult<BaseItemDto> result;
            if (string.Equals(type, RecommendationSectionHandler.MoviesSection, StringComparison.OrdinalIgnoreCase))
            {
                result = _recommendations.GetMovieResults(request);
            }
            else if (string.Equals(type, RecommendationSectionHandler.SeriesSection, StringComparison.OrdinalIgnoreCase))
            {
                result = _recommendations.GetSeriesResults(request);
            }
            else if (string.Equals(type, LiveTvType, StringComparison.OrdinalIgnoreCase))
            {
                result = _liveTv.GetLiveTvResults(request);
            }
            else
            {
                return BadRequest("type muss movies, series oder livetv sein");
            }

            return Ok(Limit(result, limit));
        }
    }
}
