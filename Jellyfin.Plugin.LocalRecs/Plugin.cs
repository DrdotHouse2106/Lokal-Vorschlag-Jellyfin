using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LocalRecs
{
    /// <summary>
    /// The main plugin class.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Plugin build version for diagnostics.
        /// Update this whenever making significant changes.
        /// </summary>
        public const string BuildVersion = "2025-12-26-v7-cold-start-fix";

        private const int MaxBackups = 10;

        // Schlüssel und Geheimnisse kommen nicht in die Sicherungen
        private static readonly string[] SecretFields = { nameof(PluginConfiguration.TmdbApiKey), nameof(PluginConfiguration.TraktClientSecret), nameof(PluginConfiguration.FanartApiKey) };

        private static readonly Guid _id = new Guid("2d2a8fdf-0593-4258-a83c-31c4b14e0110");

        private readonly IXmlSerializer _xmlSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            _xmlSerializer = xmlSerializer;
            Instance = this;
            try
            {
                var folder = BackupFolder(ConfigurationFilePath);
                if (Directory.Exists(folder))
                {
                    CleanBackups(folder);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // beim nächsten Speichern erneut
            }

            try
            {
                if (Configuration.Migrate())
                {
                    SaveConfiguration();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                // beim nächsten Start erneut
            }
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => "Quallenkino";

        /// <inheritdoc />
        public override Guid Id => _id;

        /// <inheritdoc />
        public override string Description => "Persönliche Empfehlungen, Live-TV mit eigenem Fernsehprogramm-Anbieter, Erinnerungen, Profile und eine Oberfläche im Quallenkino-Stil für Jellyfin Web";

        /// <summary>
        /// Removes the secrets (API keys, client secret) from a configuration file's content.
        /// </summary>
        /// <param name="xml">Content of the configuration file.</param>
        /// <returns>The content without secrets, or null if it is no valid XML.</returns>
        public static string? WithoutSecrets(string xml)
        {
            try
            {
                var document = XDocument.Parse(xml);
                foreach (var element in document.Descendants().Where(e => SecretFields.Contains(e.Name.LocalName, StringComparer.Ordinal)).ToList())
                {
                    element.Value = string.Empty;
                }

                return document.Declaration + Environment.NewLine + document.ToString();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
                }
            };
        }

        /// <summary>
        /// Saves the configuration – but never writes plain default values over an existing, different file.
        /// Jellyfin does exactly that when it cannot read the file (e.g. two plugin versions loaded at the same time
        /// during a rename). Before every save the previous file is kept as a backup (last 10).
        /// </summary>
        /// <param name="config">Configuration to save.</param>
        public override void SaveConfiguration(PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            var path = ConfigurationFilePath;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    // Standardwerte auch in der Form nach Migrate(), sonst würde ein fehlgeschlagenes Laden beim Start überschrieben
                    var defaults = Serialize(new PluginConfiguration());
                    var migrated = new PluginConfiguration();
                    migrated.Migrate();
                    var current = Serialize(config);
                    var existing = File.ReadAllBytes(path);
                    if ((current.SequenceEqual(defaults) || current.SequenceEqual(Serialize(migrated)))
                        && !existing.SequenceEqual(defaults) && !existing.SequenceEqual(current))
                    {
                        // Laden ist fehlgeschlagen – Datei behalten und gleich noch einmal laden
                        _ = Task.Run(ReloadAfterFailedLoadAsync);
                        return;
                    }

                    Backup(path);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Sicherung ist nur eine Zugabe – Speichern trotzdem versuchen
            }

            base.SaveConfiguration(config);
        }

        /// <summary>
        /// Gets the backups of the configuration (newest first).
        /// </summary>
        /// <returns>File names (without folder).</returns>
        public IReadOnlyList<string> GetBackups()
        {
            var folder = BackupFolder(ConfigurationFilePath);
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.xml").Select(f => Path.GetFileName(f)).OrderByDescending(f => f, StringComparer.Ordinal).ToList()
                : new List<string>();
        }

        /// <summary>
        /// Restores a backup. The backups contain no secrets – the current keys are kept.
        /// </summary>
        /// <param name="name">File name of the backup.</param>
        /// <returns>True if restored.</returns>
        public bool RestoreBackup(string name)
        {
            if (string.IsNullOrEmpty(name) || !GetBackups().Contains(name, StringComparer.Ordinal))
            {
                return false;
            }

            if (_xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), Path.Combine(BackupFolder(ConfigurationFilePath), name)) is not PluginConfiguration restored)
            {
                return false;
            }

            var current = Configuration;
            restored.TmdbApiKey = string.IsNullOrEmpty(restored.TmdbApiKey) ? current.TmdbApiKey : restored.TmdbApiKey;
            restored.TraktClientSecret = string.IsNullOrEmpty(restored.TraktClientSecret) ? current.TraktClientSecret : restored.TraktClientSecret;
            restored.FanartApiKey = string.IsNullOrEmpty(restored.FanartApiKey) ? current.FanartApiKey : restored.FanartApiKey;
            UpdateConfiguration(restored);
            return true;
        }

        private static string BackupFolder(string path) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".sicherungen");

        private static void Backup(string path)
        {
            var folder = BackupFolder(path);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".xml");
            var clean = WithoutSecrets(File.ReadAllText(path));
            if (clean != null)
            {
                File.WriteAllText(target, clean);
            }
            else
            {
                // Unlesbare Datei: unverändert sichern (genau dafür gibt es die Sicherung)
                File.Copy(path, target, true);
            }

            foreach (var old in Directory.GetFiles(folder, "*.xml").OrderByDescending(f => f, StringComparer.Ordinal).Skip(MaxBackups))
            {
                File.Delete(old);
            }

            CleanBackups(folder);
        }

        // Sicherungen älterer Versionen enthielten noch Schlüssel – nachträglich entfernen
        private static void CleanBackups(string folder)
        {
            foreach (var file in Directory.GetFiles(folder, "*.xml"))
            {
                var content = File.ReadAllText(file);
                var clean = WithoutSecrets(content);
                if (clean != null && !string.Equals(clean, content, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, clean);
                }
            }
        }

        private byte[] Serialize(PluginConfiguration config)
        {
            using var stream = new MemoryStream();
            _xmlSerializer.SerializeToStream(config, stream);
            return stream.ToArray();
        }

        private async Task ReloadAfterFailedLoadAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                try
                {
                    if (_xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), ConfigurationFilePath) is PluginConfiguration loaded)
                    {
                        Configuration = loaded;
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    // nächster Versuch
                }
            }
        }
    }
}
