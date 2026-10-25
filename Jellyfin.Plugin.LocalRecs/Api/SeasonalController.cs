using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Seasonal;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// „Passend zur Jahreszeit“ für Jellyfin Web und die TV-Apps.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Seasonal")]
    [Authorize]
    public class SeasonalController : ControllerBase
    {
        private readonly SeasonalService _seasonal;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeasonalController"/> class.
        /// </summary>
        /// <param name="seasonal">Seasonal titles.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public SeasonalController(SeasonalService seasonal, IUserManager userManager, IDtoService dtoService)
        {
            _seasonal = seasonal ?? throw new ArgumentNullException(nameof(seasonal));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        /// <summary>
        /// Gets the titles of the current season (Christmas, New Year's Eve, Halloween, Easter, optionally Valentine's Day)
        /// for the signed-in user. Outside the windows, or with fewer titles than the minimum, <c>Active</c> is false and the list empty.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone in minutes, optional (decides the day).</param>
        /// <param name="season">Shows a season outside its window (e.g. „weihnachten“, for previews), optional.</param>
        /// <returns>The season and its titles.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<SeasonalResult> Get([FromQuery] int? utcOffsetMinutes = null, [FromQuery] string? season = null)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            Response.Headers.CacheControl = "no-store";
            var current = SeasonCalendar.Get(season?.Trim().ToLowerInvariant()) ?? SeasonalService.CurrentSeason(utcOffsetMinutes);
            if (current == null)
            {
                return new SeasonalResult();
            }

            var dtos = _dtoService.GetBaseItemDtos(_seasonal.GetItems(user, current).ToList(), SeasonalSectionHandler.Options, user);
            return new SeasonalResult { Active = dtos.Count > 0, Id = current.Id, Title = current.Title, Items = dtos, TotalRecordCount = dtos.Count };
        }

        /// <summary>The season and its titles.</summary>
        public class SeasonalResult
        {
            /// <summary>Gets or sets a value indicating whether the row is shown.</summary>
            public bool Active { get; set; }

            /// <summary>Gets or sets the season ID („weihnachten“, „silvester“, „halloween“, „ostern“, „valentinstag“).</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the row title („Weihnachtsfilme“ …).</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the titles.</summary>
            public IReadOnlyList<BaseItemDto> Items { get; set; } = Array.Empty<BaseItemDto>();

            /// <summary>Gets or sets the number of titles.</summary>
            public int TotalRecordCount { get; set; }
        }
    }
}
