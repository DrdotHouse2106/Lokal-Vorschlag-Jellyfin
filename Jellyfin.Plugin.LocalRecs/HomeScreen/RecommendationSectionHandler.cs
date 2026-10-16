using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Liefert die Einträge für die Startseiten-Bereiche „Empfohlene Filme/Serien“.
    /// Wird vom Plugin „Home Screen Sections“ per Reflection aufgerufen (Klassen- und Methodenname
    /// sind Teil der Registrierung und dürfen nicht umbenannt werden).
    /// </summary>
    public class RecommendationSectionHandler
    {
        /// <summary>
        /// Value of <c>AdditionalData</c> for the movie section.
        /// </summary>
        public const string MoviesSection = "movies";

        /// <summary>
        /// Value of <c>AdditionalData</c> for the series section.
        /// </summary>
        public const string SeriesSection = "series";

        private readonly ILogger<RecommendationSectionHandler> _logger;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IDtoService _dtoService;
        private readonly RecommendationStore _store;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationSectionHandler"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="store">Recommendation store.</param>
        public RecommendationSectionHandler(
            ILogger<RecommendationSectionHandler> logger,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            IDtoService dtoService,
            RecommendationStore store)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Key that identifies the same movie/series across different library items
        /// (provider IDs first, otherwise name + year).
        /// </summary>
        /// <param name="item">Library item.</param>
        /// <returns>The key.</returns>
        public static string DuplicateKey(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            foreach (var provider in new[] { "Tmdb", "Imdb", "Tvdb", "AniDb", "AniList" })
            {
                if (item.ProviderIds != null && item.ProviderIds.TryGetValue(provider, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return provider + ":" + value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return "id:" + item.Id.ToString("N");
            }

            return "name:" + item.Name.Trim().ToLowerInvariant() + "|" + item.ProductionYear;
        }

        /// <summary>
        /// Returns the movie recommendations of the requesting user.
        /// </summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items to display.</returns>
        public QueryResult<BaseItemDto> GetMovieResults(SectionRequest request)
        {
            return GetResults(request, MoviesSection);
        }

        /// <summary>
        /// Returns the series recommendations of the requesting user.
        /// </summary>
        /// <param name="request">Request from Home Screen Sections.</param>
        /// <returns>The items to display.</returns>
        public QueryResult<BaseItemDto> GetSeriesResults(SectionRequest request)
        {
            return GetResults(request, SeriesSection);
        }

        /// <summary>
        /// Resolves the stored recommendations of a user to library items that the user can see
        /// and has not watched yet.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="section">Section type (<see cref="MoviesSection"/> or <see cref="SeriesSection"/>).</param>
        /// <returns>The items in recommendation order.</returns>
        public IReadOnlyList<BaseItem> GetItems(Guid userId, string? section)
        {
            if (userId == Guid.Empty)
            {
                return Array.Empty<BaseItem>();
            }

            var user = _userManager.GetUserById(userId);
            var recommendations = _store.Get(userId);
            if (user == null || recommendations == null)
            {
                return Array.Empty<BaseItem>();
            }

            var isSeries = string.Equals(section, SeriesSection, StringComparison.OrdinalIgnoreCase);
            var ids = isSeries ? recommendations.Series : recommendations.Movies;
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var limit = isSeries ? config.TvRecommendationCount : config.MovieRecommendationCount;

            var result = new List<BaseItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (limit > 0 && result.Count >= limit)
                {
                    break;
                }

                var item = _libraryManager.GetItemById(id);
                if (item == null || !item.IsVisible(user))
                {
                    continue;
                }

                // Inzwischen gesehene oder angefangene Titel nicht mehr anzeigen
                var userData = _userDataManager.GetUserData(user, item);
                if (item is not Series && userData != null && (userData.Played || userData.PlaybackPositionTicks > 0))
                {
                    continue;
                }

                // Gleicher Titel in mehreren Versionen/Dateien nur einmal zeigen
                if (!seen.Add(DuplicateKey(item)))
                {
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private QueryResult<BaseItemDto> GetResults(SectionRequest? request, string section)
        {
            try
            {
                var items = GetItems(request?.UserId ?? Guid.Empty, section);
                if (items.Count == 0)
                {
                    return new QueryResult<BaseItemDto>();
                }

                var user = _userManager.GetUserById(request!.UserId);
                var options = new DtoOptions
                {
                    Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview },
                    ImageTypeLimit = 1,
                    ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb },
                    EnableImages = true,
                    EnableUserData = true
                };

                var dtos = _dtoService.GetBaseItemDtos(items, options, user);
                return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Empfehlungen für die Startseite konnten nicht geladen werden");
                return new QueryResult<BaseItemDto>();
            }
        }
    }
}
