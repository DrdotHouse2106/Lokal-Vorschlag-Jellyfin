using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using LocalMediaType = Jellyfin.Plugin.LocalRecs.Models.MediaType;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Legt die Empfehlungs-Bibliotheken pro Benutzer automatisch an und setzt die Zugriffsrechte,
    /// sodass jeder Benutzer nur seine eigenen Empfehlungs-Bibliotheken sieht.
    /// </summary>
    public class LibraryProvisioningService
    {
        private readonly ILogger<LibraryProvisioningService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly VirtualLibraryManager _virtualLibraryManager;
        private readonly Func<PluginConfiguration?> _getConfiguration;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryProvisioningService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="virtualLibraryManager">Virtual library manager.</param>
        /// <param name="getConfiguration">Liefert die aktuelle Plugin-Konfiguration.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public LibraryProvisioningService(
            ILogger<LibraryProvisioningService> logger,
            ILibraryManager libraryManager,
            IUserManager userManager,
            VirtualLibraryManager virtualLibraryManager,
            Func<PluginConfiguration?> getConfiguration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
            _getConfiguration = getConfiguration ?? throw new ArgumentNullException(nameof(getConfiguration));
        }

        /// <summary>
        /// Builds the library name for a user from a template ("{user}" is replaced by the username).
        /// </summary>
        /// <param name="template">Name template.</param>
        /// <param name="username">Username.</param>
        /// <returns>The library name.</returns>
        public static string BuildLibraryName(string? template, string? username)
        {
            var name = username ?? "Unknown";
            if (string.IsNullOrWhiteSpace(template))
            {
                return name;
            }

            return template.Contains("{user}", StringComparison.OrdinalIgnoreCase)
                ? template.Replace("{user}", name, StringComparison.OrdinalIgnoreCase)
                : $"{template} ({name})";
        }

        /// <summary>
        /// Gets the suggested library name for a user and media type based on the current configuration.
        /// </summary>
        /// <param name="username">Username.</param>
        /// <param name="mediaType">Media type.</param>
        /// <returns>The library name.</returns>
        public string GetLibraryName(string? username, LocalMediaType mediaType)
        {
            var config = _getConfiguration() ?? new PluginConfiguration();
            var template = mediaType == LocalMediaType.Movie ? config.MovieLibraryNameTemplate : config.TvLibraryNameTemplate;
            return BuildLibraryName(template, username);
        }

        /// <summary>
        /// Creates missing recommendation libraries and updates library access for all users,
        /// depending on the plugin configuration.
        /// </summary>
        /// <param name="force">Run even if both automatic options are disabled (manual trigger).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The provisioning result.</returns>
        public async Task<ProvisioningResult> ProvisionAllAsync(bool force, CancellationToken cancellationToken)
        {
            var result = new ProvisioningResult();
            var config = _getConfiguration() ?? new PluginConfiguration();
            var createLibraries = force || config.AutoCreateLibraries;
            var manageAccess = config.AutoManageLibraryAccess;

            if (!createLibraries && !manageAccess)
            {
                return result;
            }

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var users = _userManager.GetUsers().ToList();
                foreach (var user in users)
                {
                    _virtualLibraryManager.EnsureUserDirectoriesExist(user.Id, user.Username);
                }

                var folders = _libraryManager.GetVirtualFolders();

                if (createLibraries)
                {
                    foreach (var user in users)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.LibrariesCreated += await EnsureUserLibrariesAsync(user, folders, config).ConfigureAwait(false);
                    }

                    if (result.LibrariesCreated > 0)
                    {
                        folders = _libraryManager.GetVirtualFolders();
                    }
                }

                if (manageAccess)
                {
                    result.UsersUpdated = await SyncLibraryAccessAsync(users, folders, cancellationToken).ConfigureAwait(false);
                }

                if (result.LibrariesCreated > 0 || result.UsersUpdated > 0)
                {
                    _logger.LogInformation(
                        "Empfehlungs-Bibliotheken eingerichtet: {Created} Bibliotheken angelegt, Zugriff für {Users} Benutzer angepasst",
                        result.LibrariesCreated,
                        result.UsersUpdated);
                }

                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Removes the recommendation libraries of a deleted user.
        /// </summary>
        /// <param name="userId">The deleted user's ID.</param>
        /// <returns>A task.</returns>
        public async Task RemoveUserLibrariesAsync(Guid userId)
        {
            var config = _getConfiguration() ?? new PluginConfiguration();
            if (!config.AutoCreateLibraries)
            {
                return;
            }

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var userPaths = new[]
                {
                    _virtualLibraryManager.GetUserLibraryPath(userId, LocalMediaType.Movie),
                    _virtualLibraryManager.GetUserLibraryPath(userId, LocalMediaType.Series)
                };

                // Only remove libraries that point exclusively to this user's recommendation folders
                var toRemove = _libraryManager.GetVirtualFolders()
                    .Where(vf => vf.Locations.Length > 0 && vf.Locations.All(loc => userPaths.Any(p => PathEquals(loc, p))))
                    .ToList();

                foreach (var folder in toRemove)
                {
                    try
                    {
                        await _libraryManager.RemoveVirtualFolder(folder.Name, false).ConfigureAwait(false);
                        _logger.LogInformation("Empfehlungs-Bibliothek {Name} des gelöschten Benutzers {UserId} entfernt", folder.Name, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Konnte Empfehlungs-Bibliothek {Name} nicht entfernen", folder.Name);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Finds the recommendation library of a user for a media type.
        /// </summary>
        /// <param name="folders">Virtual folders.</param>
        /// <param name="userId">User ID.</param>
        /// <param name="mediaType">Media type.</param>
        /// <returns>The library or null.</returns>
        public VirtualFolderInfo? FindUserLibrary(IEnumerable<VirtualFolderInfo> folders, Guid userId, LocalMediaType mediaType)
        {
            var path = _virtualLibraryManager.GetUserLibraryPath(userId, mediaType);
            return folders.FirstOrDefault(vf => vf.Locations.Any(loc => PathEquals(loc, path)));
        }

        private static bool PathEquals(string a, string b)
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(a),
                Path.TrimEndingDirectorySeparator(b),
                StringComparison.OrdinalIgnoreCase);
        }

        private static Guid ParseItemId(VirtualFolderInfo folder)
        {
            return Guid.TryParse(folder.ItemId, out var id) ? id : Guid.Empty;
        }

        private async Task<int> EnsureUserLibrariesAsync(User user, List<VirtualFolderInfo> folders, PluginConfiguration config)
        {
            var created = 0;

            foreach (var mediaType in new[] { LocalMediaType.Movie, LocalMediaType.Series })
            {
                if (FindUserLibrary(folders, user.Id, mediaType) != null)
                {
                    continue;
                }

                var path = _virtualLibraryManager.GetUserLibraryPath(user.Id, mediaType);
                var template = mediaType == LocalMediaType.Movie ? config.MovieLibraryNameTemplate : config.TvLibraryNameTemplate;
                var name = BuildLibraryName(template, user.Username);
                var collectionType = mediaType == LocalMediaType.Movie ? CollectionTypeOptions.movies : CollectionTypeOptions.tvshows;

                var options = new LibraryOptions
                {
                    PathInfos = new[] { new MediaPathInfo(path) },

                    // Die Dateien sind nur Verknüpfungen auf die echte Bibliothek: keine doppelte Bildextraktion
                    EnableChapterImageExtraction = false,
                    ExtractChapterImagesDuringLibraryScan = false,
                    EnableTrickplayImageExtraction = false,
                    ExtractTrickplayImagesDuringLibraryScan = false
                };

                try
                {
                    await _libraryManager.AddVirtualFolder(name, collectionType, options, false).ConfigureAwait(false);
                    created++;
                    _logger.LogInformation(
                        "Empfehlungs-Bibliothek {Name} für Benutzer {Username} angelegt ({Path})",
                        name,
                        user.Username,
                        path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Konnte Empfehlungs-Bibliothek {Name} für Benutzer {Username} nicht anlegen", name, user.Username);
                }
            }

            return created;
        }

        private async Task<int> SyncLibraryAccessAsync(
            List<User> users,
            List<VirtualFolderInfo> folders,
            CancellationToken cancellationToken)
        {
            var basePath = _virtualLibraryManager.VirtualLibraryBasePath;

            // Recommendation libraries per user (identified by their folder path)
            var ownRecIds = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var user in users)
            {
                var ids = new HashSet<Guid>();
                foreach (var mediaType in new[] { LocalMediaType.Movie, LocalMediaType.Series })
                {
                    var folder = FindUserLibrary(folders, user.Id, mediaType);
                    if (folder != null && ParseItemId(folder) != Guid.Empty)
                    {
                        ids.Add(ParseItemId(folder));
                    }
                }

                ownRecIds[user.Id] = ids;
            }

            // All recommendation libraries, including those of users that no longer exist
            var allRecIds = folders
                .Where(vf => vf.Locations.Length > 0 && vf.Locations.All(loc =>
                    loc.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)))
                .Select(ParseItemId)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            if (allRecIds.Count == 0)
            {
                return 0;
            }

            var regularIds = folders
                .Select(ParseItemId)
                .Where(id => id != Guid.Empty && !allRecIds.Contains(id))
                .ToHashSet();

            var updatedUsers = 0;

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var policy = _userManager.GetUserDto(user).Policy;
                if (policy == null)
                {
                    continue;
                }

                var own = ownRecIds[user.Id];
                var before = (policy.EnabledFolders ?? Array.Empty<Guid>()).ToHashSet();
                HashSet<Guid> enabled;

                if (policy.EnableAllFolders)
                {
                    // "Zugriff auf alle Bibliotheken" würde auch die Empfehlungen der anderen freigeben:
                    // auf eine explizite Liste aller vorhandenen normalen Bibliotheken plus der eigenen
                    // Empfehlungen umstellen. Später angelegte Bibliotheken gibt der Admin selbst frei.
                    enabled = new HashSet<Guid>(regularIds);
                    enabled.UnionWith(own);
                }
                else
                {
                    enabled = new HashSet<Guid>(before);
                    enabled.RemoveWhere(id => allRecIds.Contains(id) && !own.Contains(id));
                    enabled.UnionWith(own);

                    if (enabled.SetEquals(before))
                    {
                        continue;
                    }
                }

                policy.EnableAllFolders = false;
                policy.EnabledFolders = enabled.ToArray();

                try
                {
                    await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                    updatedUsers++;
                    _logger.LogInformation("Bibliothekszugriff für Benutzer {Username} angepasst", user.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Konnte Bibliothekszugriff für Benutzer {Username} nicht anpassen", user.Username);
                }
            }

            return updatedUsers;
        }
    }
}
