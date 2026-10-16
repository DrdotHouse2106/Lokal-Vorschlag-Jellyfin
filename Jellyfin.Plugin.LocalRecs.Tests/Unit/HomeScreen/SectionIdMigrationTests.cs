using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.HomeScreen
{
    /// <summary>
    /// Tests für die Section-IDs bei Home Screen Sections (müssen gültige CSS-Klassen sein).
    /// </summary>
    public class SectionIdMigrationTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString());

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Theory]
        [InlineData(HomeScreenSectionsIntegration.MoviesSectionId)]
        [InlineData(HomeScreenSectionsIntegration.SeriesSectionId)]
        [InlineData(HomeScreenSectionsIntegration.LiveTvSectionId)]
        public void SectionIds_AreValidCssClassNames(string id)
        {
            // HSS nutzt querySelector('.' + id) – die ID muss mit einem Buchstaben beginnen
            Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9_-]*$").Should().BeTrue();
        }

        [Fact]
        public void LegacyIds_MapToAllNewIds()
        {
            SectionIdMigration.LegacyIds.Values.Should().BeEquivalentTo(new[]
            {
                HomeScreenSectionsIntegration.MoviesSectionId,
                HomeScreenSectionsIntegration.SeriesSectionId,
                HomeScreenSectionsIntegration.LiveTvSectionId
            });
        }

        [Fact]
        public void MigrateUserSettingsFile_ReplacesOldIdsAndKeepsBackup()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "ModularHomeSettings.json");
            var original = "[{\"UserId\":\"4a5b6814-783b-43e7-bb97-8ae78ab31fea\",\"EnabledSections\":[\"MyMedia\",\"7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a11\",\"7D1C2A4E-4B0F-4D7C-9A51-0C6F3E2B8A13\"]}]";
            File.WriteAllText(path, original);

            SectionIdMigration.MigrateUserSettingsFile(path, NullLogger.Instance).Should().BeTrue();

            File.ReadAllText(path).Should().Be("[{\"UserId\":\"4a5b6814-783b-43e7-bb97-8ae78ab31fea\",\"EnabledSections\":[\"MyMedia\",\"LocalRecsMovies\",\"LocalRecsLiveTv\"]}]");
            File.ReadAllText(path + ".bak-localrecs").Should().Be(original);
        }

        [Fact]
        public void MigrateUserSettingsFile_NothingToDo_LeavesFileUntouched()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "ModularHomeSettings.json");
            File.WriteAllText(path, "[{\"EnabledSections\":[\"LocalRecsMovies\"]}]");

            SectionIdMigration.MigrateUserSettingsFile(path, NullLogger.Instance).Should().BeFalse();
            File.Exists(path + ".bak-localrecs").Should().BeFalse();
            SectionIdMigration.MigrateUserSettingsFile(Path.Combine(_tempDir, "missing.json"), NullLogger.Instance).Should().BeFalse();
        }
    }
}
