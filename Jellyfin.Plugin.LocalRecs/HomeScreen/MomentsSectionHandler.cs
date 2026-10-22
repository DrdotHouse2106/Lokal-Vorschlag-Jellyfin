using System;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Moments;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Startseiten-Bereiche der zeitabhängigen Zeilen für Home Screen Sections (je Zeile ein Bereich, leer wenn nichts passt).
    /// </summary>
    public class MomentsSectionHandler
    {
        private readonly MomentsService _moments;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly ILogger<MomentsSectionHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MomentsSectionHandler"/> class.
        /// </summary>
        /// <param name="moments">Time-based rows.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="logger">Logger.</param>
        public MomentsSectionHandler(MomentsService moments, IUserManager userManager, IDtoService dtoService, ILogger<MomentsSectionHandler> logger)
        {
            _moments = moments ?? throw new ArgumentNullException(nameof(moments));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>„Neu diese Woche“.</summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items.</returns>
        public QueryResult<BaseItemDto> GetNewThisWeekResults(SectionRequest request) => Results(request, MomentsService.NewThisWeek);

        /// <summary>„Vor dem Kinostart: Hol die Reihe nach“.</summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items.</returns>
        public QueryResult<BaseItemDto> GetCinemaResults(SectionRequest request) => Results(request, MomentsService.Cinema);

        /// <summary>„Heute vor X Jahren“.</summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items.</returns>
        public QueryResult<BaseItemDto> GetAnniversaryResults(SectionRequest request) => Results(request, MomentsService.Anniversary);

        /// <summary>„Vor einem Jahr hast du …“.</summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items.</returns>
        public QueryResult<BaseItemDto> GetYearAgoResults(SectionRequest request) => Results(request, MomentsService.YearAgo);

        /// <summary>„Läuft heute im TV – hier ohne Werbung“.</summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items.</returns>
        public QueryResult<BaseItemDto> GetOnTvResults(SectionRequest request) => Results(request, MomentsService.OnTvToday);

        private QueryResult<BaseItemDto> Results(SectionRequest request, string key)
        {
            try
            {
                var user = request == null ? null : _userManager.GetUserById(request.UserId);
                if (user == null)
                {
                    return new QueryResult<BaseItemDto>();
                }

                var dtos = _dtoService.GetBaseItemDtos(_moments.GetRow(user, key, null).Items.ToList(), SeasonalSectionHandler.Options, user);
                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zeitabhängige Zeile {Key} für die Startseite konnte nicht geladen werden", key);
                return new QueryResult<BaseItemDto>();
            }
        }
    }
}
