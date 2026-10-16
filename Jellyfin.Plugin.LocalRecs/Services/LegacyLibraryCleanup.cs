using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Entfernt die Empfehlungs-Bibliotheken und Symlink-Ordner, die ältere Versionen des Plugins angelegt haben.
    /// Die Medien selbst werden dabei nicht angefasst – nur die Verknüpfungen im Plugin-Ordner.
    /// </summary>
    public class LegacyLibraryCleanup
    {
        private readonly ILogger<LegacyLibraryCleanup> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly string _legacyBasePath;

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyLibraryCleanup"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="legacyBasePath">Folder that contained the per-user symlink libraries.</param>
        public LegacyLibraryCleanup(
            ILogger<LegacyLibraryCleanup> logger,
            ILibraryManager libraryManager,
            IUserManager userManager,
            string legacyBasePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _legacyBasePath = legacyBasePath ?? throw new ArgumentNullException(nameof(legacyBasePath));
        }

        /// <summary>
        /// Removes legacy recommendation libraries and their folders.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of removed libraries.</returns>
        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var legacyFolders = _libraryManager.GetVirtualFolders()
                .Where(vf => vf.Locations.Length > 0 && vf.Locations.All(IsInLegacyPath))
                .ToList();

            var removedIds = legacyFolders
                .Select(vf => Guid.TryParse(vf.ItemId, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            var removed = 0;
            foreach (var folder in legacyFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _libraryManager.RemoveVirtualFolder(folder.Name, false).ConfigureAwait(false);
                    removed++;
                    _logger.LogInformation("Alte Empfehlungs-Bibliothek {Name} entfernt", folder.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alte Empfehlungs-Bibliothek {Name} konnte nicht entfernt werden", folder.Name);
                }
            }

            if (removedIds.Count > 0)
            {
                await RemoveFromUserAccessAsync(removedIds).ConfigureAwait(false);
            }

            DeleteLegacyFolder();

            if (removed > 0)
            {
                _logger.LogInformation(
                    "{Count} alte Empfehlungs-Bibliotheken entfernt. Doppelte Einträge verschwinden nach dem nächsten Bibliotheks-Scan.",
                    removed);
            }

            return removed;
        }

        private bool IsInLegacyPath(string location)
        {
            var basePath = Path.TrimEndingDirectorySeparator(_legacyBasePath) + Path.DirectorySeparatorChar;
            return location.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
        }

        private async Task RemoveFromUserAccessAsync(System.Collections.Generic.HashSet<Guid> removedIds)
        {
            foreach (var user in _userManager.GetUsers().ToList())
            {
                try
                {
                    var policy = _userManager.GetUserDto(user).Policy;
                    if (policy?.EnabledFolders == null || !policy.EnabledFolders.Any(removedIds.Contains))
                    {
                        continue;
                    }

                    policy.EnabledFolders = policy.EnabledFolders.Where(id => !removedIds.Contains(id)).ToArray();
                    await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bibliothekszugriff von {Username} konnte nicht bereinigt werden", user.Username);
                }
            }
        }

        private void DeleteLegacyFolder()
        {
            if (!Directory.Exists(_legacyBasePath))
            {
                return;
            }

            try
            {
                // Enthält nur Verknüpfungen (Symlinks) – die Originaldateien bleiben erhalten
                Directory.Delete(_legacyBasePath, recursive: true);
                _logger.LogInformation("Alter Ordner mit Empfehlungs-Verknüpfungen gelöscht: {Path}", _legacyBasePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Alter Empfehlungs-Ordner konnte nicht gelöscht werden: {Path}", _legacyBasePath);
            }
        }
    }
}
