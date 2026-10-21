using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Admin: Sicherungen der Einstellungen auflisten und wiederherstellen. Die Sicherungen enthalten keine Schlüssel –
    /// beim Wiederherstellen bleiben die aktuellen Schlüssel erhalten.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Config/Backups")]
    [Authorize(Policy = "RequiresElevation")]
    public class ConfigBackupController : ControllerBase
    {
        /// <summary>
        /// Gets the backups (newest first).
        /// </summary>
        /// <returns>File names like "20261021-083000-123.xml" (UTC).</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IReadOnlyList<string>> GetBackups()
        {
            return Ok(Plugin.Instance?.GetBackups() ?? new List<string>());
        }

        /// <summary>
        /// Restores a backup; the current keys (TMDB, Trakt client secret, fanart.tv) are kept.
        /// </summary>
        /// <param name="name">File name of the backup.</param>
        /// <returns>No content, or 404.</returns>
        [HttpPost("{name}/Restore")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult Restore([FromRoute] string name)
        {
            return Plugin.Instance?.RestoreBackup(name) == true ? NoContent() : NotFound();
        }
    }
}
