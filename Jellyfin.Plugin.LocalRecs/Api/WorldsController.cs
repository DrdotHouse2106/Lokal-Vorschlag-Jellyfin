using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Worlds;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
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
    /// „Welten“ wie bei Disney+ (Marvel, Star Wars, Pixar …) mit persönlich sortierten Titeln für Apps.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Worlds")]
    [Authorize]
    public class WorldsController : ControllerBase
    {
        private const int DefaultLimit = 20;
        private const int MaxLimit = 100;
        private static readonly TimeSpan RecentPeriod = TimeSpan.FromDays(30);

        private readonly WorldService _worlds;
        private readonly RecommendationStore _store;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldsController"/> class.
        /// </summary>
        /// <param name="worlds">World service.</param>
        /// <param name="store">Recommendation store (personal rankings).</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public WorldsController(WorldService worlds, RecommendationStore store, IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
        {
            _worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Orders the worlds: recently watched first (most recent first), then by number of titles.
        /// </summary>
        /// <param name="worlds">Worlds with title count and last watched time.</param>
        /// <returns>The ordered worlds.</returns>
        public static IEnumerable<WorldInfo> Order(IEnumerable<(WorldInfo World, DateTime? LastWatched)> worlds)
        {
            return worlds
                .OrderByDescending(w => w.LastWatched ?? DateTime.MinValue)
                .ThenByDescending(w => w.World.ItemCount)
                .ThenBy(w => w.World.Name, StringComparer.OrdinalIgnoreCase)
                .Select(w => w.World);
        }

        /// <summary>
        /// Gets the worlds the signed-in user can see (at least <c>MinWorldItems</c> titles).
        /// </summary>
        /// <returns>The worlds, recently watched first.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<WorldInfo>> GetWorlds()
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            return Ok(Order(GetVisibleWorlds(user, _worlds.GetLastWatched(user)).Select(w => (w.Info, w.LastWatched))).ToList());
        }

        /// <summary>
        /// Gets the titles of a world, ordered for the signed-in user (next part of a started film series first,
        /// watched titles removed).
        /// </summary>
        /// <param name="worldId">World ID (e.g. "marvel").</param>
        /// <param name="limit">Maximum number of titles (default 20, max. 100).</param>
        /// <returns>The titles.</returns>
        [HttpGet("{worldId}/Recommendations")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<QueryResult<BaseItemDto>> GetWorldRecommendations([FromRoute] string worldId, [FromQuery] int? limit = null)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var (worlds, members) = _worlds.GetWorlds();
            var world = worlds.FirstOrDefault(w => string.Equals(w.Id, worldId, StringComparison.OrdinalIgnoreCase));
            if (world == null)
            {
                return NotFound();
            }

            return Ok(ToResult(user, GetItems(user, world.Id, members[world.Id], limit)));
        }

        /// <summary>
        /// Gets the world the signed-in user watched most recently (last 30 days) with its titles,
        /// e.g. for a home screen row "Mehr aus dem Marvel-Universum".
        /// </summary>
        /// <param name="limit">Maximum number of titles (default 20, max. 100).</param>
        /// <returns>The world and its titles, or 204 if nothing from a world was watched recently.</returns>
        [HttpGet("Recent")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult<RecentWorld> GetRecentWorld([FromQuery] int? limit = null)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var since = DateTime.UtcNow - RecentPeriod;
            var recent = GetVisibleWorlds(user, _worlds.GetLastWatched(user))
                .Where(w => w.LastWatched >= since)
                .OrderByDescending(w => w.LastWatched)
                .FirstOrDefault();
            if (recent.Info == null)
            {
                return NoContent();
            }

            var (_, members) = _worlds.GetWorlds();
            var items = GetItems(user, recent.Info.Id, members[recent.Info.Id], limit);
            if (items.Count == 0)
            {
                return NoContent();
            }

            return Ok(new RecentWorld { World = recent.Info, Items = ToResult(user, items).Items });
        }

        private User? CurrentUser() => _userManager.GetUserById(RecommendationsController.GetUserId(User));

        private List<(WorldInfo Info, DateTime? LastWatched)> GetVisibleWorlds(User user, IReadOnlyDictionary<Guid, DateTime> lastWatched)
        {
            var (worlds, members) = _worlds.GetWorlds();
            var minItems = Math.Max(1, Config.MinWorldItems);
            var result = new List<(WorldInfo, DateTime?)>();
            foreach (var world in worlds)
            {
                var visible = members[world.Id]
                    .Where(id => _libraryManager.GetItemById(id) is { } item && item.IsVisible(user))
                    .ToList();
                if (visible.Count < minItems)
                {
                    continue;
                }

                var watched = visible.Where(lastWatched.ContainsKey).Select(id => (DateTime?)lastWatched[id]).Max();
                result.Add((new WorldInfo
                {
                    Id = world.Id,
                    Name = world.Name,
                    ItemCount = visible.Count,
                    ImageItemId = _worlds.GetImageItem(user, visible)?.ToString("N"),
                    Studios = world.Studios.ToList(),
                    Collections = world.Collections.ToList(),
                    TmdbCollectionIds = _worlds.GetTmdbCollectionIds(world.Id).ToList()
                }, watched));
            }

            return result;
        }

        private List<BaseItem> GetItems(User user, string worldId, HashSet<Guid> members, int? limit)
        {
            var max = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

            // Persönliche Rangliste aus der letzten Berechnung, danach neue Titel nach Bewertung
            var ranked = _store.Get(user.Id)?.Worlds?.GetValueOrDefault(worldId) ?? new List<Guid>();
            var rest = members.Except(ranked)
                .Select(id => _libraryManager.GetItemById(id))
                .Where(i => i != null)
                .OrderByDescending(i => i!.CommunityRating ?? 0)
                .Select(i => i!.Id);

            var result = new List<BaseItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ranked.Where(members.Contains).Concat(rest))
            {
                if (result.Count >= max)
                {
                    break;
                }

                var item = _libraryManager.GetItemById(id);
                if (item == null || !_worlds.IsOpenFor(user, item) || !seen.Add(RecommendationSectionHandler.DuplicateKey(item)))
                {
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private QueryResult<BaseItemDto> ToResult(User user, IReadOnlyList<BaseItem> items)
        {
            if (items.Count == 0)
            {
                return new QueryResult<BaseItemDto>();
            }

            var options = new DtoOptions
            {
                Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview },
                ImageTypeLimit = 1,
                ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
                EnableImages = true,
                EnableUserData = true
            };
            var dtos = _dtoService.GetBaseItemDtos(items.ToList(), options, user);
            return new QueryResult<BaseItemDto>(null, dtos.Count, dtos);
        }

        /// <summary>
        /// A world as shown in the app.
        /// </summary>
        public class WorldInfo
        {
            /// <summary>Gets or sets the ID (e.g. "star-wars").</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the number of titles visible to the user.</summary>
            public int ItemCount { get; set; }

            /// <summary>Gets or sets the ID of a representative title with a backdrop (best rated).</summary>
            public string? ImageItemId { get; set; }

            /// <summary>Gets or sets the studios of the world definition.</summary>
            public List<string> Studios { get; set; } = new List<string>();

            /// <summary>Gets or sets the collections of the world definition.</summary>
            public List<string> Collections { get; set; } = new List<string>();

            /// <summary>Gets or sets the TMDB IDs of the collections in this world (e.g. to show missing parts via Jellyseerr).</summary>
            public List<string> TmdbCollectionIds { get; set; } = new List<string>();
        }

        /// <summary>
        /// The world watched most recently with its titles.
        /// </summary>
        public class RecentWorld
        {
            /// <summary>Gets or sets the world.</summary>
            public WorldInfo World { get; set; } = new WorldInfo();

            /// <summary>Gets or sets the titles.</summary>
            public IReadOnlyList<BaseItemDto> Items { get; set; } = Array.Empty<BaseItemDto>();
        }
    }
}
