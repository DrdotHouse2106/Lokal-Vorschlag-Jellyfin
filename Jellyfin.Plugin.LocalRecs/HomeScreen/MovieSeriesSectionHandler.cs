using System;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Startseiten-Bereich „Filmreihen für dich“ für Home Screen Sections: je Reihe der nächste Teil (angefangen) bzw.
    /// der erste ungesehene Teil, mit „Reihe · Weiter mit Teil n“ als erster Zeile der Karte.
    /// </summary>
    public class MovieSeriesSectionHandler
    {
        /// <summary>Most series in the row.</summary>
        public const int MaxSeries = 12;

        private readonly MovieSeriesService _series;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly ILogger<MovieSeriesSectionHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MovieSeriesSectionHandler"/> class.
        /// </summary>
        /// <param name="series">Movie series.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="logger">Logger.</param>
        public MovieSeriesSectionHandler(MovieSeriesService series, IUserManager userManager, IDtoService dtoService, ILogger<MovieSeriesSectionHandler> logger)
        {
            _series = series ?? throw new ArgumentNullException(nameof(series));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Returns the movie series of the requesting user (the part to watch next of each series).
        /// </summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items to display.</returns>
        public QueryResult<BaseItemDto> GetMovieSeriesResults(SectionRequest request)
        {
            try
            {
                var user = request == null ? null : _userManager.GetUserById(request.UserId);
                if (user == null)
                {
                    return new QueryResult<BaseItemDto>();
                }

                var series = _series.GetSeries(user).Take(MaxSeries).ToList();
                var options = new DtoOptions
                {
                    Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres },
                    ImageTypeLimit = 1,
                    ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
                    EnableImages = true,
                    EnableUserData = true
                };
                var dtos = _dtoService.GetBaseItemDtos(series.Select(s => s.Parts[s.NextIndex]).ToList(), options, user);
                for (var i = 0; i < dtos.Count && i < series.Count; i++)
                {
                    // Erste Zeile der Karte: Home Screen Sections zeigt dort den „Elterntitel“
                    dtos[i].SeriesName = series[i].Name + " · " + series[i].Reason;
                }

                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Filmreihen für die Startseite konnten nicht geladen werden");
                return new QueryResult<BaseItemDto>();
            }
        }
    }
}
