using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Collections;
using MediaBrowser.Controller.Dto;
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
    /// Filmreihen für Jellyfin Web und die TV-Apps: „Filmreihen für dich“, alle Teile einer Reihe, die Reihe zu einem Film
    /// und Poster bzw. Hintergrund der Reihe (auf dem Server zwischengespeichert).
    /// </summary>
    [ApiController]
    [Route("LocalRecs/MovieSeries")]
    [Authorize]
    public class MovieSeriesController : ControllerBase
    {
        private readonly MovieSeriesService _series;
        private readonly MovieSeriesLookup _lookup;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MovieSeriesController"/> class.
        /// </summary>
        /// <param name="series">Movie series.</param>
        /// <param name="lookup">Series names and images.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public MovieSeriesController(MovieSeriesService series, MovieSeriesLookup lookup, IUserManager userManager, IDtoService dtoService)
        {
            _series = series ?? throw new ArgumentNullException(nameof(series));
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        private static DtoOptions Options => new DtoOptions
        {
            Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview, ItemFields.Genres },
            ImageTypeLimit = 1,
            ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
            EnableImages = true,
            EnableUserData = true
        };

        /// <summary>
        /// Gets „Filmreihen für dich“: started series first (with the part to watch next), then series that fit the taste.
        /// </summary>
        /// <param name="limit">Most series (1–30, default 12).</param>
        /// <returns>The series.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<SeriesList> GetSeries([FromQuery] int limit = 12)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var series = _series.GetSeries(user).Take(Math.Clamp(limit, 1, 30)).ToList();
            var next = _dtoService.GetBaseItemDtos(series.Select(s => s.Parts[s.NextIndex]).ToList(), Options, user);
            Response.Headers.CacheControl = "no-store";
            return new SeriesList
            {
                Items = series.Select((s, i) => Summary(s, i < next.Count ? next[i] : null)).ToList(),
                TotalRecordCount = series.Count
            };
        }

        /// <summary>
        /// Gets a series with all parts the user can see, in release order.
        /// </summary>
        /// <param name="id">TMDB collection ID.</param>
        /// <returns>The series.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<SeriesDetail> GetSeriesById([FromRoute] string id)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var series = _series.GetSeriesById(user, id);
            return series == null ? NotFound() : Detail(series, user);
        }

        /// <summary>
        /// Gets the series a movie belongs to, with all parts (for „Weitere Teile der Reihe“ on the detail page).
        /// </summary>
        /// <param name="itemId">The movie.</param>
        /// <returns>The series, or 404 if the movie belongs to none (or fewer than two parts are in the libraries).</returns>
        [HttpGet("ByItem/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<SeriesDetail> GetSeriesOfItem([FromRoute] Guid itemId)
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var series = _series.GetSeriesOf(user, itemId);
            return series == null ? NotFound() : Detail(series, user);
        }

        /// <summary>
        /// Gets the poster or backdrop of a series (from TMDB, cached on the server). Without sign-in, so image views can load it;
        /// only for series known to the server.
        /// </summary>
        /// <param name="id">TMDB collection ID.</param>
        /// <param name="kind">„Poster“ or „Backdrop“.</param>
        /// <returns>The image.</returns>
        [HttpGet("{id}/Images/{kind}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetImage([FromRoute] string id, [FromRoute] string kind)
        {
            if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit) || id.Length > 12)
            {
                return NotFound();
            }

            var backdrop = string.Equals(kind, "Backdrop", StringComparison.OrdinalIgnoreCase);
            if (!backdrop && !string.Equals(kind, "Poster", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var image = await _lookup.GetImageAsync(id, backdrop, HttpContext.RequestAborted).ConfigureAwait(false);
            if (image == null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=604800";
            return File(image.Value.Data, image.Value.ContentType);
        }

        private User? CurrentUser() => _userManager.GetUserById(RecommendationsController.GetUserId(User));

        private SeriesSummary Summary(MovieSeriesService.MovieSeries series, BaseItemDto? next)
        {
            var info = _lookup.GetSeries(series.Id);
            return new SeriesSummary
            {
                Id = series.Id,
                Name = series.Name,
                Total = series.Parts.Count,
                Watched = series.WatchedCount,
                Started = series.Started,
                NextIndex = series.NextIndex,
                Reason = series.Reason,
                InWorld = series.InWorld,
                HasPoster = !string.IsNullOrEmpty(info?.PosterPath),
                HasBackdrop = !string.IsNullOrEmpty(info?.BackdropPath),
                Next = next,
                PartIds = series.Parts.Select(p => p.Id.ToString("N")).ToList()
            };
        }

        private SeriesDetail Detail(MovieSeriesService.MovieSeries series, User user)
        {
            var info = _lookup.GetSeries(series.Id);
            Response.Headers.CacheControl = "no-store";
            return new SeriesDetail
            {
                Id = series.Id,
                Name = series.Name,
                Total = series.Parts.Count,
                Watched = series.WatchedCount,
                NextIndex = series.NextIndex,
                HasPoster = !string.IsNullOrEmpty(info?.PosterPath),
                HasBackdrop = !string.IsNullOrEmpty(info?.BackdropPath),
                Parts = _dtoService.GetBaseItemDtos(series.Parts.ToList(), Options, user)
            };
        }

        /// <summary>„Filmreihen für dich“.</summary>
        public class SeriesList
        {
            /// <summary>Gets or sets the series in display order.</summary>
            public IReadOnlyList<SeriesSummary> Items { get; set; } = Array.Empty<SeriesSummary>();

            /// <summary>Gets or sets the number of series.</summary>
            public int TotalRecordCount { get; set; }
        }

        /// <summary>A series in the list.</summary>
        public class SeriesSummary
        {
            /// <summary>Gets or sets the TMDB collection ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the number of parts in the libraries.</summary>
            public int Total { get; set; }

            /// <summary>Gets or sets the number of watched parts.</summary>
            public int Watched { get; set; }

            /// <summary>Gets or sets a value indicating whether the user started the series.</summary>
            public bool Started { get; set; }

            /// <summary>Gets or sets the index of the part to watch next (0-based, release order).</summary>
            public int NextIndex { get; set; }

            /// <summary>Gets or sets the reason line („Weiter mit Teil 3“ or „4 Teile“).</summary>
            public string Reason { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether the series belongs to a world.</summary>
            public bool InWorld { get; set; }

            /// <summary>Gets or sets a value indicating whether a poster is available (<c>/LocalRecs/MovieSeries/{id}/Images/Poster</c>).</summary>
            public bool HasPoster { get; set; }

            /// <summary>Gets or sets a value indicating whether a backdrop is available (<c>/LocalRecs/MovieSeries/{id}/Images/Backdrop</c>).</summary>
            public bool HasBackdrop { get; set; }

            /// <summary>Gets or sets the part to watch next.</summary>
            public BaseItemDto? Next { get; set; }

            /// <summary>Gets or sets the IDs of all parts in release order.</summary>
            public IReadOnlyList<string> PartIds { get; set; } = Array.Empty<string>();
        }

        /// <summary>A series with all parts.</summary>
        public class SeriesDetail
        {
            /// <summary>Gets or sets the TMDB collection ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the number of parts in the libraries.</summary>
            public int Total { get; set; }

            /// <summary>Gets or sets the number of watched parts.</summary>
            public int Watched { get; set; }

            /// <summary>Gets or sets the index of the part to watch next (-1 = all watched).</summary>
            public int NextIndex { get; set; }

            /// <summary>Gets or sets a value indicating whether a poster is available.</summary>
            public bool HasPoster { get; set; }

            /// <summary>Gets or sets a value indicating whether a backdrop is available.</summary>
            public bool HasBackdrop { get; set; }

            /// <summary>Gets or sets the parts in release order.</summary>
            public IReadOnlyList<BaseItemDto> Parts { get; set; } = Array.Empty<BaseItemDto>();
        }
    }
}
