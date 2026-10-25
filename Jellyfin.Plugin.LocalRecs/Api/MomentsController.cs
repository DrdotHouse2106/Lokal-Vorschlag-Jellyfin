using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Moments;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Zeitabhängige Zeilen für Jellyfin Web und die TV-Apps: „Läuft heute im TV – hier ohne Werbung“, „Neu diese Woche“,
    /// „Vor dem Kinostart“, „Heute vor X Jahren“ und „Vor einem Jahr hast du …“.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Moments")]
    [Authorize]
    public class MomentsController : ControllerBase
    {
        private readonly MomentsService _moments;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MomentsController"/> class.
        /// </summary>
        /// <param name="moments">Time-based rows.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public MomentsController(MomentsService moments, IUserManager userManager, IDtoService dtoService)
        {
            _moments = moments ?? throw new ArgumentNullException(nameof(moments));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        /// <summary>
        /// Gets the time-based rows that are switched on and not empty, in display order. Each row has a key
        /// (<c>ontv</c>, <c>newthisweek</c>, <c>cinema</c>, <c>anniversary</c>, <c>yearago</c>), a title, the titles and a reason line per title.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone in minutes, optional (decides „today“).</param>
        /// <param name="key">Only this row, optional.</param>
        /// <returns>The rows.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<MomentsResult> Get([FromQuery] int? utcOffsetMinutes = null, [FromQuery] string? key = null)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            Response.Headers.CacheControl = "no-store";
            var rows = _moments.GetRows(user, utcOffsetMinutes, string.IsNullOrWhiteSpace(key) ? null : key.Trim());
            return new MomentsResult
            {
                Rows = rows.Select(r => new MomentRowDto
                {
                    Key = r.Key,
                    Title = r.Title,
                    Items = _dtoService.GetBaseItemDtos(r.Items.ToList(), SeasonalSectionHandler.Options, user),
                    Reasons = r.Reasons
                }).ToList()
            };
        }

        /// <summary>The time-based rows.</summary>
        public class MomentsResult
        {
            /// <summary>Gets or sets the rows in display order (only non-empty rows).</summary>
            public IReadOnlyList<MomentRowDto> Rows { get; set; } = Array.Empty<MomentRowDto>();

            /// <summary>Gets the number of rows (for clients that read every list like a Jellyfin query result).</summary>
            public int TotalRecordCount => Rows.Count;
        }

        /// <summary>A time-based row.</summary>
        public class MomentRowDto
        {
            /// <summary>Gets or sets the key.</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the titles.</summary>
            public IReadOnlyList<BaseItemDto> Items { get; set; } = Array.Empty<BaseItemDto>();

            /// <summary>Gets the number of titles (so a row can be read as a Jellyfin query result).</summary>
            public int TotalRecordCount => Items.Count;

            /// <summary>Gets or sets the reason line per title (item ID without dashes, e.g. „Heute 20:15 auf ZDF – hier ohne Werbung“).</summary>
            public IReadOnlyDictionary<string, string> Reasons { get; set; } = new Dictionary<string, string>();
        }
    }
}
