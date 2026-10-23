using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Upcoming;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// „Demnächst“ für Jellyfin Web und die TV-Apps: neue Folgen von Serien aus der Bibliothek (Sonarr) und bald erscheinende,
    /// vorgemerkte Filme (Radarr). Nur lesend – es wird nichts angefragt.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Upcoming")]
    [Authorize]
    public class UpcomingController : ControllerBase
    {
        private readonly UpcomingService _upcoming;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpcomingController"/> class.
        /// </summary>
        /// <param name="upcoming">Upcoming episodes and movies.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="dtoService">DTO service.</param>
        public UpcomingController(UpcomingService upcoming, IUserManager userManager, IDtoService dtoService)
        {
            _upcoming = upcoming ?? throw new ArgumentNullException(nameof(upcoming));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _dtoService = dtoService ?? throw new ArgumentNullException(nameof(dtoService));
        }

        /// <summary>
        /// Gets the upcoming episodes and movies for the signed-in user: series the user watches or likes first, then by date.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the app's time zone in minutes, optional (decides „Heute“, weekdays).</param>
        /// <returns>The entries.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<UpcomingResult>> Get([FromQuery] int? utcOffsetMinutes = null)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            var entries = await _upcoming.GetAsync(user, utcOffsetMinutes, HttpContext.RequestAborted).ConfigureAwait(false);
            var series = entries.Where(e => e.SeriesItem != null).Select(e => e.SeriesItem!).DistinctBy(i => i.Id).ToList();
            var dtos = _dtoService.GetBaseItemDtos(series, SeasonalSectionHandler.Options, user).ToDictionary(d => d.Id);
            Response.Headers.CacheControl = "no-store";
            var items = entries.Select(e => new UpcomingItem
            {
                Kind = e.Kind,
                ReleaseType = e.ReleaseType,
                Title = e.Title,
                Subtitle = e.Subtitle,
                Date = e.DateUtc,
                LocalDate = e.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Network = e.Network,
                EpisodeOverview = e.EpisodeOverview,
                Series = e.SeriesItem != null && dtos.TryGetValue(e.SeriesItem.Id, out var dto) ? dto : null,
                SeasonNumber = e.SeriesItem != null ? e.SeasonNumber : null,
                EpisodeNumber = e.SeriesItem != null ? e.EpisodeNumber : null,
                EpisodeTitle = e.EpisodeTitle,
                MoreEpisodes = e.MoreEpisodes,
                Movie = e.Movie == null ? null : new UpcomingMovie
                {
                    RadarrId = e.Movie.RadarrId,
                    TmdbId = e.Movie.TmdbId,
                    ImdbId = e.Movie.ImdbId,
                    Year = e.Movie.Year,
                    Overview = e.Movie.Overview,
                    OfficialRating = e.Movie.Certification,
                    PosterUrl = e.Movie.HasPoster ? ImagePath(e.Movie.RadarrId, "Poster") : null,
                    BackdropUrl = e.Movie.HasFanart ? ImagePath(e.Movie.RadarrId, "Backdrop") : null
                }
            }).ToList();
            return new UpcomingResult { Items = items, TotalRecordCount = items.Count };
        }

        /// <summary>
        /// Gets the poster or backdrop of an upcoming movie from Radarr. Without sign-in so image views can load it;
        /// only for movies of the current calendar.
        /// </summary>
        /// <param name="radarrId">Radarr's movie ID.</param>
        /// <param name="kind">„Poster“ or „Backdrop“.</param>
        /// <returns>The image.</returns>
        [HttpGet("Movies/{radarrId}/Images/{kind}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetImage([FromRoute] int radarrId, [FromRoute] string kind)
        {
            var backdrop = string.Equals(kind, "Backdrop", StringComparison.OrdinalIgnoreCase);
            if ((!backdrop && !string.Equals(kind, "Poster", StringComparison.OrdinalIgnoreCase)) || _upcoming.KnownMovie(radarrId) == null)
            {
                return NotFound();
            }

            var image = await _upcoming.GetImageAsync(radarrId, backdrop, HttpContext.RequestAborted).ConfigureAwait(false);
            if (image == null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=86400";
            return File(image.Value.Data, image.Value.ContentType);
        }

        /// <summary>
        /// Checks the connection to Sonarr and Radarr (admins only, for the settings page). The keys are never returned.
        /// </summary>
        /// <returns>Status per service.</returns>
        [HttpGet("Status")]
        [Authorize(Policy = "RequiresElevation")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<Dictionary<string, string>>> GetStatus()
        {
            var (sonarr, radarr) = await _upcoming.CheckAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return new Dictionary<string, string> { ["Sonarr"] = sonarr, ["Radarr"] = radarr };
        }

        private static string ImagePath(int radarrId, string kind) => "LocalRecs/Upcoming/Movies/" + radarrId.ToString(CultureInfo.InvariantCulture) + "/Images/" + kind;

        /// <summary>„Demnächst“.</summary>
        public class UpcomingResult
        {
            /// <summary>Gets or sets the entries in display order.</summary>
            public IReadOnlyList<UpcomingItem> Items { get; set; } = Array.Empty<UpcomingItem>();

            /// <summary>Gets or sets the number of entries.</summary>
            public int TotalRecordCount { get; set; }
        }

        /// <summary>An entry.</summary>
        public class UpcomingItem
        {
            /// <summary>Gets or sets the kind: „Episode“ (new episode of a series in the library) or „Movie“ (upcoming movie, not in the library yet).</summary>
            public string Kind { get; set; } = string.Empty;

            /// <summary>Gets or sets the type of the date: NewEpisode, SeasonPremiere (E01 of season 2+), SeriesPremiere (S01E01), Cinema, Digital, Physical.</summary>
            public string ReleaseType { get; set; } = string.Empty;

            /// <summary>Gets or sets the title (series or movie).</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets the line for the card („Neue Folge am Fr. 24.10.“, „Staffelstart heute“, „Kinostart morgen“, „Digital ab …“, „Auf Disc ab …“).</summary>
            public string Subtitle { get; set; } = string.Empty;

            /// <summary>Gets or sets the air or release time (UTC; for movies only the day counts).</summary>
            public DateTime Date { get; set; }

            /// <summary>Gets or sets the local day (yyyy-MM-dd) in the app's time zone.</summary>
            public string LocalDate { get; set; } = string.Empty;

            /// <summary>Gets or sets the network (episodes, if Sonarr knows it).</summary>
            public string? Network { get; set; }

            /// <summary>Gets or sets the episode overview (episodes).</summary>
            public string? EpisodeOverview { get; set; }

            /// <summary>Gets or sets the series in Jellyfin (episodes; open it on click).</summary>
            public BaseItemDto? Series { get; set; }

            /// <summary>Gets or sets the season number (episodes).</summary>
            public int? SeasonNumber { get; set; }

            /// <summary>Gets or sets the episode number (episodes).</summary>
            public int? EpisodeNumber { get; set; }

            /// <summary>Gets or sets the episode title (episodes).</summary>
            public string? EpisodeTitle { get; set; }

            /// <summary>Gets or sets the number of further new episodes of the series in the period.</summary>
            public int MoreEpisodes { get; set; }

            /// <summary>Gets or sets the movie (movies).</summary>
            public UpcomingMovie? Movie { get; set; }
        }

        /// <summary>An upcoming movie (not in Jellyfin yet).</summary>
        public class UpcomingMovie
        {
            /// <summary>Gets or sets Radarr's movie ID.</summary>
            public int RadarrId { get; set; }

            /// <summary>Gets or sets the TMDB ID.</summary>
            public string? TmdbId { get; set; }

            /// <summary>Gets or sets the IMDb ID.</summary>
            public string? ImdbId { get; set; }

            /// <summary>Gets or sets the year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets the overview.</summary>
            public string? Overview { get; set; }

            /// <summary>Gets or sets the certification.</summary>
            public string? OfficialRating { get; set; }

            /// <summary>Gets or sets the relative address of the poster (prepend the server address), or null.</summary>
            public string? PosterUrl { get; set; }

            /// <summary>Gets or sets the relative address of the backdrop (prepend the server address), or null.</summary>
            public string? BackdropUrl { get; set; }
        }
    }
}
