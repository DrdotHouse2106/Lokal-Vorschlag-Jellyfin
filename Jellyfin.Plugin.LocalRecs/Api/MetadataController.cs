using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Admin: Metadaten-Check der Filme und Serien und neuer Abruf für lückenhafte Titel.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Metadata")]
    [Authorize(Policy = "RequiresElevation")]
    public class MetadataController : ControllerBase
    {
        private readonly MetadataCheckService _check;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetadataController"/> class.
        /// </summary>
        /// <param name="check">Metadata check.</param>
        public MetadataController(MetadataCheckService check)
        {
            _check = check ?? throw new ArgumentNullException(nameof(check));
        }

        /// <summary>
        /// Checks the metadata of all movies and series.
        /// </summary>
        /// <returns>Summary per library and the titles with little metadata.</returns>
        [HttpGet("Check")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<MetadataCheckService.CheckResult> Check()
        {
            Response.Headers.CacheControl = "no-store";
            return _check.Run();
        }

        /// <summary>
        /// Queues a metadata refresh for titles (fills gaps, replaces nothing).
        /// </summary>
        /// <param name="request">The titles (at most 200).</param>
        /// <returns>The number of queued titles.</returns>
        [HttpPost("Refresh")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RefreshResult> Refresh([FromBody] RefreshRequest request)
        {
            if (request?.ItemIds == null || request.ItemIds.Count == 0)
            {
                return BadRequest();
            }

            return new RefreshResult { Queued = _check.Refresh(request.ItemIds.Where(id => id != Guid.Empty)) };
        }

        /// <summary>Titles to refresh.</summary>
        public class RefreshRequest
        {
            /// <summary>Gets or sets the item IDs.</summary>
            public IReadOnlyList<Guid>? ItemIds { get; set; }
        }

        /// <summary>Result of a refresh request.</summary>
        public class RefreshResult
        {
            /// <summary>Gets or sets the number of queued titles.</summary>
            public int Queued { get; set; }
        }
    }
}
