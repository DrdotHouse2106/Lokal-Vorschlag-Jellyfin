using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Hosting;
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

        // Größte Rangliste, die eine Anfrage braucht (limit 50 × 3 wegen der Sichtbarkeit)
        private const int RankedMax = 150;

        private static readonly TimeSpan RankingCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<(bool Movies, bool Series, int Days), (DateTime Created, IReadOnlyList<Guid> Ranked)> RankingCache = new();
        private static readonly SemaphoreSlim RankingGate = new SemaphoreSlim(1, 1);

        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IDtoService _dtoService;
        private readonly ILogger<Top10Controller> _logger;
        private readonly CancellationToken _stopping;

        /// <summary>
        /// Initializes a new instance of the <see cref="Top10Controller"/> class.
        /// </summary>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="dtoService">DTO service.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="lifetime">Application lifetime (stops the background computation on shutdown), optional.</param>
        public Top10Controller(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            IDtoService dtoService,
            ILogger<Top10Controller> logger,
            IHostApplicationLifetime? lifetime = null)
        {
            _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
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
        public async Task<ActionResult<QueryResult<BaseItemDto>>> GetTop10([FromQuery] string type = "all", [FromQuery] int days = 60, [FromQuery] int limit = 10)
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

            var ranked = await GetRankingAsync(includeMovies, includeSeries, Math.Clamp(days, 1, 365), since, HttpContext.RequestAborted).ConfigureAwait(false);

            // Mehr Kandidaten ranken, weil der anfragende Benutzer nicht alle Titel sehen darf
            var items = ranked
                .Take(limit * 3)
                .Select(id => _libraryManager.GetItemById(id))
                .Where(item => item != null && item.IsVisibleStandalone(requestingUser))
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

        // Serverweite Rangliste je Typ und Zeitraum, 10 Minuten frisch; nur eine Rechnung zur Zeit. Ist eine ältere
        // Rangliste da, wird sie sofort ausgeliefert und im Hintergrund neu gerechnet – keine Anfrage wartet auf eine andere.
        private async Task<IReadOnlyList<Guid>> GetRankingAsync(bool includeMovies, bool includeSeries, int days, DateTime since, CancellationToken cancellationToken)
        {
            var key = (includeMovies, includeSeries, days);
            if (RankingCache.TryGetValue(key, out var cached))
            {
                if (DateTime.UtcNow - cached.Created >= RankingCacheDuration && RankingGate.Wait(0))
                {
                    var stopping = _stopping;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            RankingCache[key] = (DateTime.UtcNow, ComputeRanking(includeMovies, includeSeries, DateTime.UtcNow.AddDays(-days), stopping));
                        }
                        catch (OperationCanceledException)
                        {
                            // Server fährt herunter
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Top 10: Rangliste konnte nicht neu gerechnet werden");
                        }
                        finally
                        {
                            RankingGate.Release();
                        }
                    });
                }

                return cached.Ranked;
            }

            await RankingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (RankingCache.TryGetValue(key, out cached))
                {
                    return cached.Ranked;
                }

                var ranked = ComputeRanking(includeMovies, includeSeries, since, cancellationToken);
                RankingCache[key] = (DateTime.UtcNow, ranked);
                return ranked;
            }
            finally
            {
                RankingGate.Release();
            }
        }

        private IReadOnlyList<Guid> ComputeRanking(bool includeMovies, bool includeSeries, DateTime since, CancellationToken cancellationToken)
        {
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
                cancellationToken.ThrowIfCancellationRequested();
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

                        var itemKey = item is Episode episode ? episode.SeriesId : item.Id;
                        if (itemKey != Guid.Empty)
                        {
                            usages.Add((itemKey, user.Id, data.PlayCount));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Top 10: Verlauf eines Benutzers konnte nicht gelesen werden");
                }
            }

            return Rank(usages, RankedMax);
        }
    }
}
