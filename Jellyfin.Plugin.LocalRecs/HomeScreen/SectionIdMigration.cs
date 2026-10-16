using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Stellt in den Einstellungen von „Home Screen Sections“ die alten Section-IDs (GUIDs) auf die neuen IDs um.
    /// Die alten IDs begannen mit einer Ziffer und waren dadurch keine gültigen CSS-Klassen – HSS hat deshalb
    /// die komplette Startseite nicht mehr aufgebaut (bis Version 12.3.2).
    /// </summary>
    public static class SectionIdMigration
    {
        /// <summary>
        /// Old section IDs (≤ 12.3.2) mapped to the new IDs.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> LegacyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a11"] = HomeScreenSectionsIntegration.MoviesSectionId,
            ["7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a12"] = HomeScreenSectionsIntegration.SeriesSectionId,
            ["7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a13"] = HomeScreenSectionsIntegration.LiveTvSectionId
        };

        /// <summary>
        /// Replaces the old IDs in the per-user settings file of Home Screen Sections (ModularHomeSettings.json).
        /// </summary>
        /// <param name="filePath">Path of the settings file.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>True if the file was changed.</returns>
        public static bool MigrateUserSettingsFile(string filePath, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);

            if (!File.Exists(filePath))
            {
                return false;
            }

            var text = File.ReadAllText(filePath);
            var migrated = text;
            foreach (var (oldId, newId) in LegacyIds)
            {
                migrated = migrated.Replace(oldId, newId, StringComparison.OrdinalIgnoreCase);
            }

            if (migrated == text)
            {
                return false;
            }

            File.Copy(filePath, filePath + ".bak-localrecs", true);
            File.WriteAllText(filePath, migrated);
            logger.LogInformation("Section-IDs in den Benutzer-Einstellungen von Home Screen Sections umgestellt: {Path}", filePath);
            return true;
        }

        /// <summary>
        /// Replaces the old IDs in the admin settings of Home Screen Sections (SectionSettings) via reflection
        /// and saves the configuration.
        /// </summary>
        /// <param name="homeScreenSectionsAssembly">The loaded Home Screen Sections assembly.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>True if the configuration was changed.</returns>
        public static bool MigrateAdminSettings(Assembly homeScreenSectionsAssembly, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(homeScreenSectionsAssembly);
            ArgumentNullException.ThrowIfNull(logger);

            var pluginType = homeScreenSectionsAssembly.GetType("Jellyfin.Plugin.HomeScreenSections.HomeScreenSectionsPlugin");
            var plugin = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var configuration = plugin?.GetType().GetProperty("Configuration")?.GetValue(plugin);
            var settingsProperty = configuration?.GetType().GetProperty("SectionSettings");
            if (configuration == null || settingsProperty == null || settingsProperty.GetValue(configuration) is not Array settings)
            {
                return false;
            }

            var elementType = settings.GetType().GetElementType();
            var idProperty = elementType?.GetProperty("SectionId");
            if (elementType == null || idProperty == null)
            {
                return false;
            }

            var entries = settings.Cast<object>().ToList();
            var existingIds = entries.Select(e => idProperty.GetValue(e) as string).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new List<object>();
            var changed = false;

            foreach (var entry in entries)
            {
                var id = idProperty.GetValue(entry) as string;
                if (id != null && LegacyIds.TryGetValue(id, out var newId))
                {
                    changed = true;
                    if (existingIds.Contains(newId))
                    {
                        continue; // neue ID ist schon eingestellt – alten Eintrag verwerfen
                    }

                    idProperty.SetValue(entry, newId);
                    existingIds.Add(newId);
                }

                result.Add(entry);
            }

            if (!changed)
            {
                return false;
            }

            var newArray = Array.CreateInstance(elementType, result.Count);
            for (var i = 0; i < result.Count; i++)
            {
                newArray.SetValue(result[i], i);
            }

            settingsProperty.SetValue(configuration, newArray);
            plugin!.GetType().GetMethod("SaveConfiguration", Type.EmptyTypes)?.Invoke(plugin, null);
            logger.LogInformation("Section-IDs in den Einstellungen von Home Screen Sections umgestellt");
            return true;
        }
    }
}
