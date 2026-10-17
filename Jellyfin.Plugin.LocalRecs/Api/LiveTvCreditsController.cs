using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Besetzung von Live-TV-Sendungen aus der XMLTV-Quelle (Jellyfin selbst speichert für EPG-Sendungen
    /// keine Personen). Für Apps, die auf der Infoseite einer Sendung die Darsteller zeigen möchten.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/LiveTv")]
    [Authorize]
    public class LiveTvCreditsController : ControllerBase
    {
        private const int MaxIds = 200;

        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly XmltvSource _xmltv;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvCreditsController"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="xmltv">XMLTV source.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public LiveTvCreditsController(ILibraryManager libraryManager, IUserManager userManager, XmltvSource xmltv)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _xmltv = xmltv ?? throw new ArgumentNullException(nameof(xmltv));
        }

        /// <summary>
        /// Builds the credits of a program from an XMLTV programme.
        /// </summary>
        /// <param name="programId">Program ID.</param>
        /// <param name="programme">XMLTV data, or null if there is no match.</param>
        /// <returns>The credits.</returns>
        public static ProgramCredits ToCredits(Guid programId, XmltvSource.XmltvProgramme? programme)
        {
            if (programme == null)
            {
                return new ProgramCredits { ProgramId = programId.ToString("N") };
            }

            return new ProgramCredits
            {
                ProgramId = programId.ToString("N"),
                Matched = true,
                Actors = programme.Actors.Select((name, i) => new ActorCredit { Name = name, Role = i < programme.Roles.Count ? programme.Roles[i] : null }).ToList(),
                Directors = programme.Directors.ToList(),
                Writers = programme.Writers.ToList(),
                Year = programme.Year,
                Country = programme.Country,
                Episode = programme.Episode,
                ImdbId = programme.ImdbId,
                TmdbId = programme.TmdbId
            };
        }

        /// <summary>
        /// Gets actors, directors, writers, year and country of a live TV program.
        /// </summary>
        /// <param name="programId">Jellyfin ID of the program.</param>
        /// <returns>The credits; <c>Matched = false</c> if the XMLTV source has no data for this program.</returns>
        [HttpGet("Program/{programId}/Credits")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<ProgramCredits> GetCredits([FromRoute] Guid programId)
        {
            if (!HasLiveTvAccess())
            {
                return Forbid();
            }

            var program = _libraryManager.GetItemById(programId) as LiveTvProgram;
            if (program == null)
            {
                return NotFound();
            }

            return Ok(ToCredits(programId, _xmltv.Lookup(program.StartDate, program.Name, program.EpisodeTitle)));
        }

        /// <summary>
        /// Gets the credits of several live TV programs at once (max. 200).
        /// </summary>
        /// <param name="ids">Comma-separated program IDs.</param>
        /// <returns>Credits by program ID; unknown IDs are left out.</returns>
        [HttpGet("Credits")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public ActionResult<Dictionary<string, ProgramCredits>> GetCreditsForPrograms([FromQuery] string? ids)
        {
            if (!HasLiveTvAccess())
            {
                return Forbid();
            }

            var programIds = (ids ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (programIds.Count == 0 || programIds.Count > MaxIds)
            {
                return BadRequest("ids: 1 bis " + MaxIds + " Sendungs-IDs, durch Komma getrennt");
            }

            var result = new Dictionary<string, ProgramCredits>();
            foreach (var id in programIds)
            {
                if (_libraryManager.GetItemById(id) is LiveTvProgram program)
                {
                    result[id.ToString("N")] = ToCredits(id, _xmltv.Lookup(program.StartDate, program.Name, program.EpisodeTitle));
                }
            }

            return Ok(result);
        }

        private bool HasLiveTvAccess()
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            return user != null && user.HasPermission(PermissionKind.EnableLiveTvAccess);
        }

        /// <summary>
        /// Credits of a live TV program.
        /// </summary>
        public class ProgramCredits
        {
            /// <summary>Gets or sets the program ID.</summary>
            public string ProgramId { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether the XMLTV source has data for the program.</summary>
            public bool Matched { get; set; }

            /// <summary>Gets or sets the actors with roles.</summary>
            public List<ActorCredit> Actors { get; set; } = new List<ActorCredit>();

            /// <summary>Gets or sets the directors.</summary>
            public List<string> Directors { get; set; } = new List<string>();

            /// <summary>Gets or sets the writers.</summary>
            public List<string> Writers { get; set; } = new List<string>();

            /// <summary>Gets or sets the production year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets the country.</summary>
            public string? Country { get; set; }

            /// <summary>Gets or sets the episode number as shown on screen (e.g. "S11 E8/12").</summary>
            public string? Episode { get; set; }

            /// <summary>Gets or sets the IMDb ID.</summary>
            public string? ImdbId { get; set; }

            /// <summary>Gets or sets the TMDb ID.</summary>
            public string? TmdbId { get; set; }
        }

        /// <summary>
        /// An actor and the role played.
        /// </summary>
        public class ActorCredit
        {
            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the role, if known.</summary>
            public string? Role { get; set; }
        }
    }
}
