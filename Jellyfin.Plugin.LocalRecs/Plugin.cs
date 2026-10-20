using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                    var defaults = Serialize(new PluginConfiguration());
                    if (Serialize(config).SequenceEqual(defaults) && !File.ReadAllBytes(path).SequenceEqual(defaults))
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

        private static void Backup(string path)
        {
            var folder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".sicherungen");
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".xml"), true);
            foreach (var old in Directory.GetFiles(folder, "*.xml").OrderByDescending(f => f, StringComparer.Ordinal).Skip(MaxBackups))
            {
                File.Delete(old);
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
