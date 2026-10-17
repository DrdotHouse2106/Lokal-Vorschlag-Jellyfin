using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// „Top 10 bei euch“: die in letzter Zeit meistgesehenen Filme und Serien über alle Benutzer des Servers.
    /// Liefert nur Titel, die der angemeldete Benutzer sehen darf; keine Benutzernamen oder Einzeldaten.
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    [Authorize]
    public class Top10Controller : ControllerBase
    {
        private const int PlayedItemsPerUser = 300;

        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IDtoService _dtoService;
        private readonly ILogger<Top10Controller> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Top10Controller"/> class.
        /// </summary>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="logger">Logger.</param>
        public Top10Controller(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            IDtoService dtoService,
            ILogger<Top10Controller> logger)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Ranks titles: more different viewers first, then more plays.
        /// </summary>
        /// <param name="usages">Played entries (title key, user, plays).</param>
        /// <param name="limit">Maximum number of titles.</param>
        /// <returns>Title keys in rank order.</returns>
        public static IReadOnlyList<Guid> Rank(IEnumerable<(Guid Key, Guid UserId, int Plays)> usages, int limit)
        {
            ArgumentNullException.ThrowIfNull(usages);

            return usages
                .GroupBy(u => u.Key)
                .Select(g => (Key: g.Key, Viewers: g.Select(u => u.UserId).Distinct().Count(), Plays: g.Sum(u => Math.Max(1, u.Plays))))
                .OrderByDescending(x => x.Viewers)
                .ThenByDescending(x => x.Plays)
                .Take(Math.Max(1, limit))
                .Select(x => x.Key)
                .ToList();
        }

        /// <summary>
        /// Gets the most watched movies and series of all users.
        /// </summary>
        /// <param name="type">"all", "movies" or "series".</param>
        /// <param name="days">Period in days (1–365).</param>
        /// <param name="limit">Number of titles (1–50).</param>
        /// <returns>The titles in rank order.</returns>
        [HttpGet("Top10")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<QueryResult<BaseItemDto>> GetTop10([FromQuery] string type = "all", [FromQuery] int days = 60, [FromQuery] int limit = 10)
        {
            var requestingUser = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (requestingUser == null)
            {
                return Unauthorized();
            }

            var includeMovies = !string.Equals(type, "series", StringComparison.OrdinalIgnoreCase);
            var includeSeries = !string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase);
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            limit = Math.Clamp(limit, 1, 50);

            var kinds = new List<BaseItemKind>();
            if (includeMovies)
            {
                kinds.Add(BaseItemKind.Movie);
            }

            if (includeSeries)
            {
                kinds.Add(BaseItemKind.Episode);
            }

            var usages = new List<(Guid Key, Guid UserId, int Plays)>();
            foreach (var user in _userManager.GetUsers().ToList())
            {
                try
                {
                    var played = _libraryManager.GetItemList(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = kinds.ToArray(),
                        IsPlayed = true,
                        Recursive = true,
                        OrderBy = new[] { (ItemSortBy.DatePlayed, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                        Limit = PlayedItemsPerUser
                    });

                    foreach (var item in played)
                    {
                        var data = _userDataManager.GetUserData(user, item);
                        if (data?.LastPlayedDate is not DateTime lastPlayed || lastPlayed < since)
                        {
                            continue;
                        }

                        var key = item is Episode episode ? episode.SeriesId : item.Id;
                        if (key != Guid.Empty)
                        {
                            usages.Add((key, user.Id, data.PlayCount));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Top 10: Verlauf eines Benutzers konnte nicht gelesen werden");
                }
            }

            // Mehr Kandidaten ranken, weil der anfragende Benutzer nicht alle Titel sehen darf
            var items = Rank(usages, limit * 3)
                .Select(id => _libraryManager.GetItemById(id))
                .Where(item => item != null && item.IsVisible(requestingUser))
                .Take(limit)
                .Cast<BaseItem>()
                .ToList();

            var options = new DtoOptions
            {
                Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview },
                ImageTypeLimit = 1,
                ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
                EnableImages = true,
                EnableUserData = true
            };

            var dtos = _dtoService.GetBaseItemDtos(items, options, requestingUser);
            return Ok(new QueryResult<BaseItemDto>(null, dtos.Count, dtos));
        }
    }
}
