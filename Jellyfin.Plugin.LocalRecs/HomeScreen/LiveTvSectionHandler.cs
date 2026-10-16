using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalRecs.Configuration;
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
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvSectionHandler"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="recommendations">Live TV recommendation service.</param>
        /// <param name="libraryManager">Library manager.</param>
        public LiveTvSectionHandler(
            ILogger<LiveTvSectionHandler> logger,
            IUserManager userManager,
            IDtoService dtoService,
            LiveTvRecommendationService recommendations,
            ILibraryManager libraryManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
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
                AddBroadcastTimes(dtos);
                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TV-Empfehlungen für die Startseite konnten nicht geladen werden");
                return new QueryResult<BaseItemDto>();
            }
        }

        /// <summary>
        /// Home Screen Sections shows the name and a second line on each card. For programs the second line
        /// is the episode title, so the broadcast time and channel are written there (only in this response,
        /// the program itself is not changed).
        /// </summary>
        private void AddBroadcastTimes(IReadOnlyList<BaseItemDto> dtos)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var timeZone = LiveTvDisplay.ResolveTimeZone(config.LiveTvTimeZone);
            var now = DateTime.UtcNow;

            foreach (var dto in dtos)
            {
                if (dto.StartDate == null)
                {
                    continue;
                }

                var channelName = dto.ChannelName;
                if (string.IsNullOrWhiteSpace(channelName) && dto.ChannelId.HasValue)
                {
                    channelName = _libraryManager.GetItemById(dto.ChannelId.Value)?.Name;
                }

                dto.Name = LiveTvDisplay.Title(dto.Name, dto.EpisodeTitle, LiveTvText.DetectSport(dto.Genres, dto.Name, dto.EpisodeTitle) != null || dto.IsSports == true);
                dto.EpisodeTitle = LiveTvDisplay.TimeLine(dto.StartDate.Value, dto.EndDate, channelName, now, timeZone);
                dto.IndexNumber = null;
                dto.ParentIndexNumber = null;
            }
        }
    }
}
