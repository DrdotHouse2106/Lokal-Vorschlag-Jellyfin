using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Banner;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Banner des Tages („Billboard“) für die Startseite von Jellyfin Web und den TV-Apps: täglich neu, am Tag auf allen
    /// Geräten gleich, gemischt aus Entdecken und Weiterschauen. Dazu „Später“: einen Titel für einige Wochen aus dem Banner nehmen.
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    [Authorize]
    public class BannerController : ControllerBase
    {
        private readonly BannerService _banner;
        private readonly BannerStore _store;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerController"/> class.
        /// </summary>
        /// <param name="banner">Banner service.</param>
        /// <param name="store">Banner store („Später“).</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public BannerController(BannerService banner, BannerStore store, IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
        {
            _banner = banner ?? throw new ArgumentNullException(nameof(banner));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        /// <summary>
        /// Gets the banner of today for the signed-in user (movies and series with backdrop, in display order).
        /// Same as <c>GET /LocalRecs/Billboard</c>.
        /// </summary>
        /// <param name="count">Number of titles (1–12, default 5).</param>
        /// <param name="limit">Alias of <paramref name="count"/>.</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone in minutes east of UTC (day change, weekday evening), optional.</param>
        /// <returns>The titles; <c>Reasons</c> names titles that fit right now („Weiterschauen“, „Neue Folgen“, „Fortsetzen“).</returns>
        [HttpGet("Banner")]
        [HttpGet("Billboard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<BannerResult> Get([FromQuery] int? count = null, [FromQuery] int? limit = null, [FromQuery] int? utcOffsetMinutes = null)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            var picked = _banner.GetBanner(user, utcOffsetMinutes, count ?? limit ?? BannerPicker.DefaultCount);
            var options = new DtoOptions
            {
                Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres },
                ImageTypeLimit = 1,
                ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
                EnableImages = true,
                EnableUserData = true
            };
            var dtos = _dtoService.GetBaseItemDtos(picked.Select(p => p.Item).ToList(), options, user);
            Response.Headers.CacheControl = "no-store";
            return new BannerResult
            {
                Items = dtos,
                TotalRecordCount = dtos.Count,
                Reasons = picked.Where(p => p.Reason.Length > 0).ToDictionary(p => p.Item.Id.ToString("N"), p => p.Reason)
            };
        }

        /// <summary>
        /// „Später“: takes a title out of the banner for some weeks. It stays in the recommendations and does not count as rejection.
        /// Same as <c>POST /LocalRecs/Billboard/Later</c>.
        /// </summary>
        /// <param name="request">Title and days (default 21).</param>
        /// <returns>No content.</returns>
        [HttpPost("Banner/Later")]
        [HttpPost("Billboard/Later")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult Later([FromBody] LaterRequest request)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            if (request == null || !_banner.Later(user, request.ItemId, request.Days ?? BannerService.DefaultLaterDays))
            {
                return NotFound();
            }

            return NoContent();
        }

        /// <summary>
        /// Gets the titles the signed-in user took out of the banner, with the time until when.
        /// </summary>
        /// <returns>The titles.</returns>
        [HttpGet("Banner/Later")]
        [HttpGet("Billboard/Later")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IReadOnlyList<LaterEntry>> GetLater()
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            Response.Headers.CacheControl = "no-store";
            return _store.GetLater(userId, DateTime.UtcNow)
                .OrderBy(e => e.Value)
                .Select(e => new LaterEntry { ItemId = e.Key.ToString("N"), Name = _libraryManager.GetItemById(e.Key)?.Name ?? string.Empty, Until = e.Value })
                .ToList();
        }

        /// <summary>
        /// Brings a title back into the banner.
        /// </summary>
        /// <param name="itemId">The title.</param>
        /// <returns>No content.</returns>
        [HttpDelete("Banner/Later/{itemId}")]
        [HttpDelete("Billboard/Later/{itemId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult RemoveLater([FromRoute] Guid itemId)
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            _store.RemoveLater(userId, itemId);
            return NoContent();
        }

        /// <summary>Banner of the day.</summary>
        public class BannerResult
        {
            /// <summary>Gets or sets the titles in display order.</summary>
            public IReadOnlyList<BaseItemDto> Items { get; set; } = Array.Empty<BaseItemDto>();

            /// <summary>Gets or sets the number of titles.</summary>
            public int TotalRecordCount { get; set; }

            /// <summary>Gets or sets a short label for titles that fit right now, by item ID without dashes.</summary>
            public IReadOnlyDictionary<string, string> Reasons { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>„Später“ for a title.</summary>
        public class LaterRequest
        {
            /// <summary>Gets or sets the title.</summary>
            public Guid ItemId { get; set; }

            /// <summary>Gets or sets the days out of the banner (1–90, default 21).</summary>
            public int? Days { get; set; }
        }

        /// <summary>A title out of the banner.</summary>
        public class LaterEntry
        {
            /// <summary>Gets or sets the item ID.</summary>
            public string ItemId { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets until when (UTC).</summary>
            public DateTime Until { get; set; }
        }
    }
}
