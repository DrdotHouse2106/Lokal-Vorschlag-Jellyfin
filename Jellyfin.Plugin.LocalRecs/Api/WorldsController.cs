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
        private readonly Discover.DiscoverService? _discover;
        private readonly MissingRanker? _ranker;

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
        /// <param name="discover">TMDB titles for missing titles, optional.</param>
        /// <param name="ranker">Personal order of missing titles, optional.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public WorldsController(WorldService worlds, RecommendationStore store, IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService, WorldListSource listSource, WorldLogoService logos, Discover.DiscoverService? discover = null, MissingRanker? ranker = null)
        {
            _discover = discover;
            _ranker = ranker;
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
        /// Gets a value indicating whether a missing title can be offered for requesting: no placeholder („Untitled …“ or
        /// without a real name), a known date or year, and no short film (movies under 40 minutes – otherwise Disney and
        /// Pixar would list endless shorts).
        /// </summary>
        /// <param name="item">The title from TMDB.</param>
        /// <returns>True if it is shown.</returns>
        public static bool IsRequestable(Discover.DiscoverItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrWhiteSpace(item.Title) || System.Text.RegularExpressions.Regex.IsMatch(item.Title, @"^\s*untitled\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrEmpty(item.ReleaseDate) && item.Year == null)
            {
                return false;
            }

            return item.MediaType == "tv" || item.RuntimeMinutes is not (> 0 and < 40);
        }

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
        /// <param name="sort">"year" (newest first, default), "year-asc", "name", "rating", "added" (recently added first) or "chrono" (in-story order of the world's chronology, since 12.55.0).</param>
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
            var accessible = _worlds.AccessibleItems(user);
            IEnumerable<BaseItem> items = members[world.Id]
                .Where(accessible.ContainsKey)
                .Select(id => accessible[id])
                .Where(i => string.IsNullOrEmpty(type)
                    || (string.Equals(type, "movies", StringComparison.OrdinalIgnoreCase) && i is MediaBrowser.Controller.Entities.Movies.Movie)
                    || (string.Equals(type, "series", StringComparison.OrdinalIgnoreCase) && i is MediaBrowser.Controller.Entities.TV.Series));

            // Handlungszeit: Reihenfolge der Chronologie (jeder Titel einmal, an seiner ersten Stelle), übrige Titel nach Jahr dahinter
            var chronoOrder = new Dictionary<Guid, int>();
            if (string.Equals(sort, "chrono", StringComparison.OrdinalIgnoreCase) && _worlds.GetMembership().Chronologies.TryGetValue(world.Id, out var chronology))
            {
                foreach (var entry in chronology.Where(e => e.ItemId != null))
                {
                    chronoOrder.TryAdd(entry.ItemId!.Value, chronoOrder.Count);
                }
            }

            items = sort?.ToLowerInvariant() switch
            {
                "chrono" => items.OrderBy(i => chronoOrder.TryGetValue(i.Id, out var position) ? position : int.MaxValue).ThenBy(i => ReleaseDate(i) ?? DateTime.MaxValue),
                "name" => items.OrderBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase),
                "added" => items.OrderByDescending(i => i.DateCreated).ThenBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase),
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
        /// Gets the titles of the world list that are not in the library (with TMDB data like the discover rows: German
        /// title, line, FSK, genres, providers, German trailer), for requesting via Jellyseerr. Titles above the user's
        /// parental rating are left out. <c>Chronology</c> is the world's in-universe order with the missing titles at
        /// their place (<c>Missing: true</c>); every entry has its <c>Index</c>, each missing title its <c>ChronologyIndex</c>.
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The missing titles; <c>Enabled</c> is false if switched off.</returns>
        [HttpGet("{worldId}/Missing")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async System.Threading.Tasks.Task<ActionResult<MissingResult>> GetMissing([FromRoute] string worldId, System.Threading.CancellationToken cancellationToken)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var membership = _worlds.GetMembership();
            var world = membership.Worlds.FirstOrDefault(w => w.HasId(worldId));
            if (world == null)
            {
                return NotFound();
            }

            var accessible = _worlds.AccessibleItems(user);
            var (enabled, byKey) = await LoadMissingAsync(user, membership, world, cancellationToken).ConfigureAwait(false);
            var chronology = BuildMissingChronology(membership, world.Id, accessible.Keys.ToHashSet(), byKey);
            foreach (var entry in chronology ?? new List<MissingChronologyEntry>())
            {
                if (entry.TmdbId is int tmdb && byKey.TryGetValue((tmdb, entry.MediaType == "tv"), out var item) && item.ChronologyIndex == null)
                {
                    item.ChronologyIndex = entry.Index;
                }
            }

            // Persönlich sortiert (die Chronologie behält ihre eigene Reihenfolge)
            return new MissingResult { Enabled = enabled, Items = Personal(user, byKey.Values), Chronology = chronology };
        }

        /// <summary>
        /// Gets the normal world view in sections (like the rows of the brand tiles on Disney+): per section the titles in the
        /// library the user can see (newest first) and – if switched on – the missing titles, ordered for the user
        /// (<c>MatchScore</c>, <c>Reason</c>); titles with few TMDB votes have <c>Minor: true</c> and come last
        /// (apps show them under „Alle anzeigen“). Empty if the world has no sections; the chronology is not affected.
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The sections in display order.</returns>
        [HttpGet("{worldId}/Sections")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async System.Threading.Tasks.Task<ActionResult<SectionsResult>> GetSections([FromRoute] string worldId, System.Threading.CancellationToken cancellationToken)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var membership = _worlds.GetMembership();
            var world = membership.Worlds.FirstOrDefault(w => w.HasId(worldId));
            if (world == null)
            {
                return NotFound();
            }

            if (world.Sections.Count == 0 || !membership.SectionOf.TryGetValue(world.Id, out var sectionOf))
            {
                return new SectionsResult();
            }

            var accessible = _worlds.AccessibleItems(user);
            var (enabled, byKey) = await LoadMissingAsync(user, membership, world, cancellationToken).ConfigureAwait(false);
            var missing = Personal(user, byKey.Values);
            var result = new List<WorldSectionInfo>();
            foreach (var section in world.Sections)
            {
                var itemIds = sectionOf
                    .Where(kv => kv.Value == section.Name && accessible.ContainsKey(kv.Key))
                    .Select(kv => accessible[kv.Key])
                    .OrderByDescending(i => ReleaseDate(i) ?? DateTime.MinValue)
                    .ThenBy(i => i.SortName ?? i.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => i.Id.ToString("N"))
                    .ToList();
                var sectionMissing = missing.Where(m => m.Section == section.Name).OrderBy(m => m.Minor).ToList();
                if (itemIds.Count > 0 || sectionMissing.Count > 0)
                {
                    result.Add(new WorldSectionInfo { Key = WorldDefinition.ToId(section.Name), Title = section.Name, ItemIds = itemIds, Missing = sectionMissing });
                }
            }

            return new SectionsResult { MissingEnabled = enabled, Sections = result };
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
            foreach (var (name, value) in Security.ImageSafety.ResponseHeaders)
            {
                Response.Headers[name] = value;
            }

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

        // Chronologie mit vorhandenen (sichtbaren) und fehlenden Titeln, jeder Eintrag mit Index; Abschnitte ohne Titel fallen weg
        private static List<MissingChronologyEntry>? BuildMissingChronology(
            WorldMembership membership,
            string worldId,
            HashSet<Guid> visible,
            Dictionary<(int TmdbId, bool Series), Discover.DiscoverItem> missing)
        {
            if (!membership.Chronologies.TryGetValue(worldId, out var chronology))
            {
                return null;
            }

            var result = new List<MissingChronologyEntry>();
            string? pendingSection = null;
            foreach (var entry in chronology)
            {
                if (entry.Section != null)
                {
                    pendingSection = entry.Section;
                    continue;
                }

                MissingChronologyEntry? title = null;
                var seasons = entry.Seasons.Count > 0 ? entry.Seasons.ToList() : null;
                if (entry.ItemId is Guid id && visible.Contains(id))
                {
                    title = new MissingChronologyEntry { ItemId = id.ToString("N"), Label = entry.Label, Seasons = seasons };
                }
                else if (entry.Missing is { } absent && missing.ContainsKey((absent.TmdbId, absent.Series)))
                {
                    title = new MissingChronologyEntry { Missing = true, TmdbId = absent.TmdbId, MediaType = absent.Series ? "tv" : "movie", Label = entry.Label, Seasons = seasons };
                }

                if (title == null)
                {
                    continue;
                }

                if (pendingSection != null)
                {
                    result.Add(new MissingChronologyEntry { Index = result.Count, Section = pendingSection });
                    pendingSection = null;
                }

                title.Index = result.Count;
                result.Add(title);
            }

            return result.Count > 0 ? result : null;
        }

        // Fehlende Titel einer Welt mit TMDB-Daten: nur anfragbare, Altersfreigabe geprüft, mit Abschnitt und „Minor“
        private async System.Threading.Tasks.Task<(bool Enabled, Dictionary<(int TmdbId, bool Series), Discover.DiscoverItem> Items)> LoadMissingAsync(
            User user,
            WorldMembership membership,
            WorldDefinition world,
            System.Threading.CancellationToken cancellationToken)
        {
            var missing = membership.Missing.TryGetValue(world.Id, out var list) ? list : new List<WorldMembership.MissingTitle>();
            var enabled = Config.WorldsShowMissing && _discover != null;
            var details = enabled && missing.Count > 0
                ? await _discover!.GetTitlesAsync(missing.Select(m => (m.TmdbId, m.Series)).ToList(), cancellationToken).ConfigureAwait(false)
                : Array.Empty<Discover.DiscoverItem>();
            var sectionOf = missing.GroupBy(m => (m.TmdbId, m.Series)).ToDictionary(g => g.Key, g => g.First().Section);
            var minVotes = world.Sections.ToDictionary(x => x.Name, x => x.MinMissingVotes, StringComparer.Ordinal);

            // Nur anfragbare Titel (kein Platzhalter, mit Datum, kein Kurzfilm); Altersfreigabe wie bei den Entdecken-Reihen
            var byKey = details
                .Where(IsRequestable)
                .Where(d => (d.MediaType == "tv" ? new MediaBrowser.Controller.Entities.TV.Series { Name = d.Title, OfficialRating = d.OfficialRating } as BaseItem : new MediaBrowser.Controller.Entities.Movies.Movie { Name = d.Title, OfficialRating = d.OfficialRating }).IsParentalAllowed(user, true))
                .GroupBy(d => (d.TmdbId, d.MediaType == "tv"))
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var (key, item) in byKey)
            {
                item.Section = sectionOf.GetValueOrDefault(key);
                item.Minor = item.Section != null && minVotes.GetValueOrDefault(item.Section) is int min
                    && (item.Released ? item.VoteCount < min : item.Popularity < 10);
            }

            return (enabled, byKey);
        }

        // Persönliche Reihenfolge (Geschmack, gesehene Teile, Suchen, TMDB-Bewertung); ohne Dienst nach Jahr, neueste zuerst
        private List<Discover.DiscoverItem> Personal(User user, IEnumerable<Discover.DiscoverItem> items)
        {
            if (_ranker == null)
            {
                return items.OrderByDescending(i => i.Year ?? 0).ToList();
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return MissingRanker.Rank(items, _ranker.GetProfile(user), today);
        }

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

        private User? CurrentUser() => RequestUser.Resolve(this, _userManager);

        private List<(WorldInfo Info, DateTime? LastWatched)> GetVisibleWorlds(User user, IReadOnlyDictionary<Guid, DateTime> lastWatched)
        {
            var hasFanart = !string.IsNullOrWhiteSpace(Config.FanartApiKey);
            var listSource = WorldListSource.ConfiguredSource();
            var result = new List<(WorldInfo, DateTime?)>();
            foreach (var entry in _worlds.GetVisibleWorlds(user, Math.Max(1, Config.MinWorldItems)))
            {
                var world = entry.World;
                var watched = entry.Visible.Where(lastWatched.ContainsKey).Select(id => (DateTime?)lastWatched[id]).Max();
                var trailer = _worlds.GetTrailer(user, world.Id, entry.Visible);
                result.Add((new WorldInfo
                {
                    Id = world.Id,
                    Name = world.Name,
                    ItemCount = entry.Visible.Count,
                    ImageItemId = entry.ImageItemId?.ToString("N"),
                    Studios = world.Studios.ToList(),
                    Collections = world.Collections.ToList(),
                    TmdbCollectionIds = _worlds.GetTmdbCollectionIds(world.Id).ToList(),
                    LogoUrl = WorldLogoService.ResolveAll(world.Logos, listSource, hasFanart).Count > 0 ? "/LocalRecs/Worlds/" + world.Id + "/Logo" : null,
                    TrailerItemId = trailer?.Item.Id.ToString("N"),
                    TrailerItemName = trailer?.Item.Name,
                    TrailerId = trailer?.Trailer.Id.ToString("N"),
                    Chronology = Chronology(world.Id, entry.Visible),
                    HasSections = world.Sections.Count > 0
                }, watched));
            }

            return result;
        }

        // Reihenfolge nach Handlungszeit mit den Titeln, die der Benutzer sehen darf; Abschnitte ohne Titel fallen weg
        private List<ChronologyEntry>? Chronology(string worldId, IReadOnlyCollection<Guid> visible)
        {
            if (!_worlds.GetMembership().Chronologies.TryGetValue(worldId, out var chronology))
            {
                return null;
            }

            var result = new List<ChronologyEntry>();
            ChronologyEntry? pendingSection = null;
            foreach (var entry in chronology)
            {
                if (entry.Section != null)
                {
                    pendingSection = new ChronologyEntry { Section = entry.Section };
                    continue;
                }

                if (entry.ItemId is not Guid id || !visible.Contains(id))
                {
                    continue;
                }

                if (pendingSection != null)
                {
                    result.Add(pendingSection);
                    pendingSection = null;
                }

                result.Add(new ChronologyEntry { ItemId = id.ToString("N"), Label = entry.Label, Seasons = entry.Seasons.Count > 0 ? entry.Seasons.ToList() : null });
            }

            return result.Any(e => e.ItemId != null) ? result : null;
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

            /// <summary>Gets or sets the ID of a title with a local trailer (set in the list, otherwise most recently added), or null.</summary>
            public string? TrailerItemId { get; set; }

            /// <summary>Gets or sets the name of that title.</summary>
            public string? TrailerItemName { get; set; }

            /// <summary>Gets or sets the ID of its first local trailer (playable like any video, e.g. /Videos/{id}/stream), or null.</summary>
            public string? TrailerId { get; set; }

            /// <summary>
            /// Gets or sets the order by in-story time (since 12.55.0), or null if the world has none – then apps show no switch.
            /// The order of the list is the story order; a series may appear several times with different seasons.
            /// </summary>
            public List<ChronologyEntry>? Chronology { get; set; }

            /// <summary>Gets or sets a value indicating whether the world has sections for the normal view (<c>/LocalRecs/Worlds/{id}/Sections</c>).</summary>
            public bool HasSections { get; set; }
        }

        /// <summary>The normal world view in sections.</summary>
        public class SectionsResult
        {
            /// <summary>Gets or sets a value indicating whether missing titles are switched on.</summary>
            public bool MissingEnabled { get; set; }

            /// <summary>Gets or sets the sections in display order (empty if the world has none).</summary>
            public IReadOnlyList<WorldSectionInfo> Sections { get; set; } = Array.Empty<WorldSectionInfo>();
        }

        /// <summary>A section of the world view.</summary>
        public class WorldSectionInfo
        {
            /// <summary>Gets or sets the key, e.g. „realverfilmungen“.</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the title, e.g. „Realverfilmungen“.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the titles in the library the user can see (Jellyfin IDs, newest first).</summary>
            public IReadOnlyList<string> ItemIds { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets the missing titles, ordered for the user; <c>Minor</c> ones last.</summary>
            public IReadOnlyList<Discover.DiscoverItem> Missing { get; set; } = Array.Empty<Discover.DiscoverItem>();
        }

        /// <summary>Titles of a world that are not in the library.</summary>
        public class MissingResult
        {
            /// <summary>Gets or sets a value indicating whether the feature is switched on (setting „Welten: fehlende Titel zeigen“).</summary>
            public bool Enabled { get; set; }

            /// <summary>Gets or sets the missing titles (same fields as the discover rows, plus <c>ChronologyIndex</c>).</summary>
            public IReadOnlyList<Discover.DiscoverItem> Items { get; set; } = Array.Empty<Discover.DiscoverItem>();

            /// <summary>Gets or sets the in-universe order with present and missing titles, or null.</summary>
            public IReadOnlyList<MissingChronologyEntry>? Chronology { get; set; }

            /// <summary>Gets the number of missing titles.</summary>
            public int TotalRecordCount => Items.Count;
        }

        /// <summary>An entry of the chronology with missing titles: section, library title or missing title.</summary>
        public class MissingChronologyEntry
        {
            /// <summary>Gets or sets the position in the list (0-based).</summary>
            public int Index { get; set; }

            /// <summary>Gets or sets the section heading (only for sections).</summary>
            public string? Section { get; set; }

            /// <summary>Gets or sets the Jellyfin ID (titles in the library).</summary>
            public string? ItemId { get; set; }

            /// <summary>Gets or sets a value indicating whether the title is missing in the library.</summary>
            public bool Missing { get; set; }

            /// <summary>Gets or sets the TMDB ID (missing titles).</summary>
            public int? TmdbId { get; set; }

            /// <summary>Gets or sets the media type „movie“ or „tv“ (missing titles).</summary>
            public string? MediaType { get; set; }

            /// <summary>Gets or sets the time label, e.g. „2151“.</summary>
            public string? Label { get; set; }

            /// <summary>Gets or sets the seasons (series), null for the whole series.</summary>
            public List<int>? Seasons { get; set; }
        }

        /// <summary>
        /// An entry of the order by in-story time: a section heading or a title of the library.
        /// </summary>
        public class ChronologyEntry
        {
            /// <summary>Gets or sets the section heading (e.g. „Kelvin-Zeitlinie (alternativ)“), only in section entries.</summary>
            public string? Section { get; set; }

            /// <summary>Gets or sets the Jellyfin ID of the title (without dashes), only in title entries.</summary>
            public string? ItemId { get; set; }

            /// <summary>Gets or sets the ready text, e.g. „2151“, or null.</summary>
            public string? Label { get; set; }

            /// <summary>Gets or sets the seasons (series only), or null for the whole series.</summary>
            public List<int>? Seasons { get; set; }
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

            /// <summary>Gets the number of titles (so the answer can be read as a Jellyfin query result).</summary>
            public int TotalRecordCount => Items.Count;
        }
    }
}
