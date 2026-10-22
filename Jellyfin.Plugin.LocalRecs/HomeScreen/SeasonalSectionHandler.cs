using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Seasonal;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Startseiten-Bereich „Passend zur Jahreszeit“ für Home Screen Sections: außerhalb der Zeitfenster leer.
    /// </summary>
    public class SeasonalSectionHandler
    {
        private readonly SeasonalService _seasonal;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly ILogger<SeasonalSectionHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SeasonalSectionHandler"/> class.
        /// </summary>
        /// <param name="seasonal">Seasonal titles.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="logger">Logger.</param>
        public SeasonalSectionHandler(SeasonalService seasonal, IUserManager userManager, IDtoService dtoService, ILogger<SeasonalSectionHandler> logger)
        {
            _seasonal = seasonal ?? throw new ArgumentNullException(nameof(seasonal));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Gets the options for the item DTOs.</summary>
        public static DtoOptions Options => new DtoOptions
        {
            Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres },
            ImageTypeLimit = 1,
            ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
            EnableImages = true,
            EnableUserData = true
        };

        /// <summary>
        /// Returns the titles of the current season for the requesting user (empty outside the windows).
        /// </summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items to display.</returns>
        public QueryResult<BaseItemDto> GetSeasonalResults(SectionRequest request)
        {
            try
            {
                var user = request == null ? null : _userManager.GetUserById(request.UserId);
                var season = SeasonalService.CurrentSeason(null);
                if (user == null || season == null)
                {
                    return new QueryResult<BaseItemDto>();
                }

                var dtos = _dtoService.GetBaseItemDtos(_seasonal.GetItems(user, season).ToList(), Options, user);
                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Titel zur Jahreszeit für die Startseite konnten nicht geladen werden");
                return new QueryResult<BaseItemDto>();
            }
        }
    }
}
