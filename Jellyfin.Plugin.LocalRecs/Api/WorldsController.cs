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
        private readonly WorldListSource _listSource;
        private readonly WorldLogoService _logos;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldsController"/> class.
        /// </summary>
        /// <param name="worlds">World service.</param>
        /// <param name="store">Recommendation store (personal rankings).</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="listSource">Maintained world list.</param>
        /// <param name="logos">World logos.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public WorldsController(WorldService worlds, RecommendationStore store, IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService, WorldListSource listSource, WorldLogoService logos)
        {
            _logos = logos ?? throw new ArgumentNullException(nameof(logos));
            _listSource = listSource ?? throw new ArgumentNullException(nameof(listSource));
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
            var world = worlds.FirstOrDefault(w => w.HasId(worldId));
            if (world == null)
            {
                return NotFound();
            }

            return Ok(ToResult(user, GetItems(user, world.Id, members[world.Id], limit)));
        }

        /// <summary>
        /// Gets all titles of a world the signed-in user can see (also watched ones), e.g. for the world page.
        /// Contains every title of the world: from studios, collections and the list (TMDB/IMDb/TVDB IDs).
        /// </summary>
        /// <param name="worldId">World ID (or a former ID).</param>
        /// <param name="sort">"year" (newest first, default), "year-asc", "name" or "rating".</param>
        /// <param name="type">Optional filter: "movies" or "series".</param>
        /// <param name="startIndex">Index of the first title (paging).</param>
        /// <param name="limit">Maximum number of titles (default 100, max. 500).</param>
        /// <returns>The titles.</returns>
        [HttpGet("{worldId}/Items")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<QueryResult<BaseItemDto>> GetWorldItems(
            [FromRoute] string worldId,
            [FromQuery] string? sort = null,
            [FromQuery] string? type = null,
            [FromQuery] int? startIndex = null,
            [FromQuery] int? limit = null)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var (worlds, members) = _worlds.GetWorlds();
            var world = worlds.FirstOrDefault(w => w.HasId(worldId));
            if (world == null)
            {
                return NotFound();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<BaseItem> items = members[world.Id]
                .Select(id => _libraryManager.GetItemById(id))
                .Where(i => i != null && i.IsVisible(user))
                .Select(i => i!)
                .Where(i => string.IsNullOrEmpty(type)
                    || (string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase) && i is MediaBrowser.Controller.Entities.Movies.Movie)
                    || (string.Equals(type, "series", StringComparison.OrdinalIgnoreCase) && i is MediaBrowser.Controller.Entities.TV.Series));
            items = sort?.ToLowerInvariant() switch
            {
                "name" => items.OrderBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase),
                "rating" => items.OrderByDescending(i => i.CommunityRating ?? 0).ThenBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase),
                "year-asc" => items.OrderBy(i => ReleaseDate(i) ?? DateTime.MaxValue).ThenBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase),
                _ => items.OrderByDescending(i => ReleaseDate(i) ?? DateTime.MinValue).ThenBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase)
            };

            var all = items.Where(i => seen.Add(RecommendationSectionHandler.DuplicateKey(i))).ToList();
            var page = all.Skip(Math.Max(0, startIndex ?? 0)).Take(Math.Clamp(limit ?? 100, 1, 500)).ToList();
            var result = ToResult(user, page);
            return Ok(new QueryResult<BaseItemDto>(startIndex, all.Count, result.Items));
        }

        /// <summary>
        /// Gets the logo of a world (no sign-in needed, so image loaders can fetch it directly).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The image, or 404 if the world has no logo.</returns>
        [HttpGet("{worldId}/Logo")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async System.Threading.Tasks.Task<ActionResult> GetLogo([FromRoute] string worldId, System.Threading.CancellationToken cancellationToken)
        {
            var world = _worlds.GetMembership().Worlds.FirstOrDefault(w => w.HasId(worldId));
            var fanartKey = Config.FanartApiKey?.Trim();
            var sources = WorldLogoService.ResolveAll(world?.Logos, WorldListSource.ConfiguredSource(), !string.IsNullOrEmpty(fanartKey));
            if (world == null || sources.Count == 0)
            {
                return NotFound();
            }

            var logo = await _logos.GetFirstAsync(world.Id, sources, fanartKey, cancellationToken).ConfigureAwait(false);
            if (logo == null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=86400";
            return File(logo.Data, logo.ContentType);
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

        /// <summary>
        /// Admin: state of the world list and number of titles per world.
        /// </summary>
        /// <returns>The overview.</returns>
        [HttpGet("Admin")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<AdminOverview> GetAdminOverview()
        {
            var membership = _worlds.GetMembership();
            var source = WorldListSource.ConfiguredSource();
            return Ok(new AdminOverview
            {
                ListSource = string.IsNullOrEmpty(source) ? null : source,
                ListLoaded = _listSource.LastLoaded,
                ListError = _listSource.LastError,
                ListFromCache = _listSource.FromCache,
                UsesList = !string.IsNullOrEmpty(source) && _listSource.GetWorlds() != null,
                Worlds = membership.Worlds.Select(w => new AdminWorld
                {
                    Id = w.Id,
                    Name = w.Name,
                    ItemCount = membership.Members[w.Id].Count,
                    Includes = w.Includes.Count,
                    Excludes = w.Excludes.Count
                }).ToList()
            });
        }

        /// <summary>
        /// Admin: titles of a world with the reason they belong to it, and suggestions for missing titles
        /// (with a ready-made line for the list).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <returns>The details.</returns>
        [HttpGet("Admin/{worldId}")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<AdminWorldDetails> GetAdminWorld([FromRoute] string worldId)
        {
            var membership = _worlds.GetMembership();
            var world = membership.Worlds.FirstOrDefault(w => w.HasId(worldId));
            if (world == null)
            {
                return NotFound();
            }

            return Ok(new AdminWorldDetails
            {
                Id = world.Id,
                Name = world.Name,
                Items = membership.Sources[world.Id].Select(kv => ToAdminTitle(membership, kv.Key, kv.Value)).Where(t => t != null).Select(t => t!).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                Suggestions = membership.Suggest(world.Id).Select(s => ToAdminTitle(membership, s.ItemId, s.Reason)).Where(t => t != null).Select(t => t!).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        /// <summary>
        /// Admin: loads the world list again (e.g. after editing it on GitHub).
        /// </summary>
        /// <returns>The overview after loading.</returns>
        [HttpPost("Admin/Reload")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async System.Threading.Tasks.Task<ActionResult<AdminOverview>> ReloadList()
        {
            await _listSource.ReloadAsync().ConfigureAwait(false);
            _worlds.Invalidate();
            return GetAdminOverview();
        }

        private static DateTime? ReleaseDate(BaseItem item) =>
            item.PremiereDate ?? (item.ProductionYear is int year and > 0 ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null);

        private AdminTitle? ToAdminTitle(WorldMembership membership, Guid itemId, string reason)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null || !membership.Items.TryGetValue(itemId, out var entry))
            {
                return null;
            }

            var type = entry.IsSeries ? "serie" : "film";
            var reference = !string.IsNullOrEmpty(entry.Tmdb) ? "\"tmdb\": \"" + entry.Tmdb + "\""
                : !string.IsNullOrEmpty(entry.Imdb) ? "\"imdb\": \"" + entry.Imdb + "\""
                : !string.IsNullOrEmpty(entry.Tvdb) ? "\"tvdb\": \"" + entry.Tvdb + "\""
                : null;
            return new AdminTitle
            {
                Id = itemId.ToString("N"),
                Name = item.Name ?? string.Empty,
                Year = item.ProductionYear,
                Type = type,
                Tmdb = entry.Tmdb,
                Imdb = entry.Imdb,
                Reason = reason,
                ListEntry = reference == null ? null : "{ " + reference + ", \"typ\": \"" + type + "\", \"name\": " + System.Text.Json.JsonSerializer.Serialize(item.Name ?? string.Empty) + " }"
            };
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
                    TmdbCollectionIds = _worlds.GetTmdbCollectionIds(world.Id).ToList(),
                    LogoUrl = WorldLogoService.ResolveAll(world.Logos, WorldListSource.ConfiguredSource(), !string.IsNullOrWhiteSpace(Config.FanartApiKey)).Count > 0 ? "/LocalRecs/Worlds/" + world.Id + "/Logo" : null
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

            /// <summary>Gets or sets the logo path on this server ("/LocalRecs/Worlds/{id}/Logo"), or null without logo.</summary>
            public string? LogoUrl { get; set; }
        }

        /// <summary>
        /// Admin overview of the worlds.
        /// </summary>
        public class AdminOverview
        {
            /// <summary>Gets or sets the configured list (URL or path), null if none.</summary>
            public string? ListSource { get; set; }

            /// <summary>Gets or sets when the list was loaded (UTC).</summary>
            public DateTime? ListLoaded { get; set; }

            /// <summary>Gets or sets the last load error.</summary>
            public string? ListError { get; set; }

            /// <summary>Gets or sets a value indicating whether the local copy is used.</summary>
            public bool ListFromCache { get; set; }

            /// <summary>Gets or sets a value indicating whether the worlds come from the list (otherwise from the text rules).</summary>
            public bool UsesList { get; set; }

            /// <summary>Gets or sets the worlds.</summary>
            public List<AdminWorld> Worlds { get; set; } = new List<AdminWorld>();
        }

        /// <summary>
        /// A world in the admin overview.
        /// </summary>
        public class AdminWorld
        {
            /// <summary>Gets or sets the ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the number of titles in the library (all users).</summary>
            public int ItemCount { get; set; }

            /// <summary>Gets or sets the number of explicitly listed titles.</summary>
            public int Includes { get; set; }

            /// <summary>Gets or sets the number of excluded titles.</summary>
            public int Excludes { get; set; }
        }

        /// <summary>
        /// Titles of a world for the admin view.
        /// </summary>
        public class AdminWorldDetails
        {
            /// <summary>Gets or sets the ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the titles of the world.</summary>
            public List<AdminTitle> Items { get; set; } = new List<AdminTitle>();

            /// <summary>Gets or sets titles that probably belong to the world.</summary>
            public List<AdminTitle> Suggestions { get; set; } = new List<AdminTitle>();
        }

        /// <summary>
        /// A title in the admin view.
        /// </summary>
        public class AdminTitle
        {
            /// <summary>Gets or sets the item ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the production year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets the type ("film" or "serie").</summary>
            public string Type { get; set; } = string.Empty;

            /// <summary>Gets or sets the TMDB ID.</summary>
            public string? Tmdb { get; set; }

            /// <summary>Gets or sets the IMDb ID.</summary>
            public string? Imdb { get; set; }

            /// <summary>Gets or sets why the title belongs (or probably belongs) to the world.</summary>
            public string Reason { get; set; } = string.Empty;

            /// <summary>Gets or sets a ready-made entry for "titel" or "ausschluss" in welten.json.</summary>
            public string? ListEntry { get; set; }
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
