using System;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Liefert die Einträge für den Startseiten-Bereich „Heute im TV für dich“.
    /// Wird vom Plugin „Home Screen Sections“ per Reflection aufgerufen.
    /// </summary>
    public class LiveTvSectionHandler
    {
        private readonly ILogger<LiveTvSectionHandler> _logger;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly LiveTvRecommendationService _recommendations;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvSectionHandler"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="recommendations">Live TV recommendation service.</param>
        public LiveTvSectionHandler(
            ILogger<LiveTvSectionHandler> logger,
            IUserManager userManager,
            IDtoService dtoService,
            LiveTvRecommendationService recommendations)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
        }

        /// <summary>
        /// Returns the recommended upcoming live TV programs of the requesting user.
        /// </summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The programs to display.</returns>
        public QueryResult<BaseItemDto> GetLiveTvResults(SectionRequest request)
        {
            try
            {
                var userId = request?.UserId ?? Guid.Empty;
                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    return new QueryResult<BaseItemDto>();
                }

                var items = _recommendations.GetRecommendedPrograms(userId);
                var options = new DtoOptions
                {
                    Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.ChannelInfo },
                    ImageTypeLimit = 1,
                    ImageTypes = new[] { ImageType.Primary, ImageType.Thumb, ImageType.Backdrop },
                    EnableImages = true,
                    EnableUserData = true
                };

                var dtos = _dtoService.GetBaseItemDtos(items, options, user);
                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TV-Empfehlungen für die Startseite konnten nicht geladen werden");
                return new QueryResult<BaseItemDto>();
            }
        }
    }
}
