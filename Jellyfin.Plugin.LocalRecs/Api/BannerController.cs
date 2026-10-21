using System;
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
    /// Banner des Tages für die Startseite (Jellyfin Web und TV-Apps): täglich neu, am Tag auf allen Geräten gleich.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Banner")]
    [Authorize]
    public class BannerController : ControllerBase
    {
        private readonly BannerService _banner;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="BannerController"/> class.
        /// </summary>
        /// <param name="banner">Banner service.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public BannerController(BannerService banner, IUserManager userManager, IDtoService dtoService)
        {
            _banner = banner ?? throw new ArgumentNullException(nameof(banner));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        /// <summary>
        /// Gets the banner of today for the signed-in user: movies and series from the best recommendations, weighted at
        /// random but fixed for the day; titles from the banner of the last days and long ignored recommendations come up less often.
        /// </summary>
        /// <param name="count">Number of titles (1–10, default 5).</param>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone in minutes (decides when the day changes), optional.</param>
        /// <returns>The titles in banner order.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<QueryResult<BaseItemDto>> Get([FromQuery] int count = BannerPicker.DefaultCount, [FromQuery] int? utcOffsetMinutes = null)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            var items = _banner.GetBanner(user, utcOffsetMinutes, count);
            var options = new DtoOptions
            {
                Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres },
                ImageTypeLimit = 1,
                ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
                EnableImages = true,
                EnableUserData = true
            };
            var dtos = _dtoService.GetBaseItemDtos(items, options, user);
            Response.Headers.CacheControl = "no-store";
            return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
        }
    }
}
