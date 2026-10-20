using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Räumt beim Start ältere Plugin-Ordner mit derselben Plugin-ID auf. Jellyfin erkennt alte Versionen nur
    /// am gleichen Namen – nach einer Umbenennung (z. B. „Local Recommendations“ → „Quallenkino“) bleiben sie
    /// sonst liegen und werden zusätzlich geladen (doppelte Schnittstellen).
    /// </summary>
    public class PluginFolderCleanup : IHostedService
    {
        private const string DllName = "Jellyfin.Plugin.LocalRecs.dll";

        private readonly ILogger<PluginFolderCleanup> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginFolderCleanup"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        public PluginFolderCleanup(ILogger<PluginFolderCleanup> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Reads the version of a plugin folder belonging to this plugin (meta.json or the DLL itself).
        /// </summary>
        /// <param name="directory">Plugin folder.</param>
        /// <param name="pluginId">ID of this plugin.</param>
        /// <returns>The version, or null if the folder belongs to another plugin.</returns>
        public static Version? OwnVersion(string directory, Guid pluginId)
        {
            var meta = Path.Combine(directory, "meta.json");
            if (File.Exists(meta))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(meta));
                    var root = document.RootElement;
                    if (root.TryGetProperty("guid", out var guid) && Guid.TryParse(guid.GetString(), out var id) && id == pluginId
                        && root.TryGetProperty("version", out var version) && Version.TryParse(version.GetString(), out var parsed))
                    {
                        return parsed;
                    }

                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    return null;
                }
            }

            var dll = Path.Combine(directory, DllName);
            if (!File.Exists(dll))
            {
                return null;
            }

            try
            {
                return AssemblyName.GetAssemblyName(dll).Version;
            }
            catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is ArgumentException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                Clean();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Alte Plugin-Ordner konnten nicht aufgeräumt werden");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private void Clean()
        {
            var own = Path.GetDirectoryName(typeof(PluginFolderCleanup).Assembly.Location);
            var parent = own == null ? null : Path.GetDirectoryName(own);
            var current = typeof(PluginFolderCleanup).Assembly.GetName().Version;
            var pluginId = Plugin.Instance?.Id ?? Guid.Empty;
            if (parent == null || current == null || pluginId == Guid.Empty || !Directory.Exists(parent))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(parent))
            {
                if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(own!), StringComparison.Ordinal))
                {
                    continue;
                }

                var version = OwnVersion(directory, pluginId);
                if (version == null)
                {
                    continue;
                }

                if (version >= current)
                {
                    _logger.LogWarning("Neuere Version {Version} dieses Plugins liegt in {Path} – diese Version ({Current}) ist veraltet", version, directory, current);
                    continue;
                }

                try
                {
                    Directory.Delete(directory, true);
                    _logger.LogWarning("Alte Version {Version} dieses Plugins entfernt ({Path}). Falls sie noch geladen war: Jellyfin bitte einmal neu starten.", version, directory);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Alte Version {Version} in {Path} konnte nicht gelöscht werden", version, directory);
                }
            }
        }
    }
}
