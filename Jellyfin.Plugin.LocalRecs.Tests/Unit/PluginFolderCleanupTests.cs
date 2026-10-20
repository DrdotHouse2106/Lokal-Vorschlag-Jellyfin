using System;
using System.IO;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für das Erkennen alter Plugin-Ordner (auch unter früherem Namen).
    /// </summary>
    public class PluginFolderCleanupTests
    {
        private static readonly Guid Id = Guid.Parse("2d2a8fdf-0593-4258-a83c-31c4b14e0110");

        [Fact]
        public void OwnVersion_ReadsMetaJson_OfSamePluginOnly()
        {
            var root = Path.Combine(Path.GetTempPath(), "lr-cleanup-" + Guid.NewGuid().ToString("N"));
            var own = Path.Combine(root, "Local Recommendations_12.26.0.0");
            var other = Path.Combine(root, "TMDb_12.2.0.0");
            var empty = Path.Combine(root, "leer");
            Directory.CreateDirectory(own);
            Directory.CreateDirectory(other);
            Directory.CreateDirectory(empty);
            try
            {
                File.WriteAllText(Path.Combine(own, "meta.json"), "{\"guid\":\"2d2a8fdf-0593-4258-a83c-31c4b14e0110\",\"name\":\"Local Recommendations\",\"version\":\"12.26.0.0\"}");
                File.WriteAllText(Path.Combine(other, "meta.json"), "{\"guid\":\"" + Guid.NewGuid() + "\",\"version\":\"12.2.0.0\"}");

                PluginFolderCleanup.OwnVersion(own, Id).Should().Be(new Version(12, 26, 0, 0));
                PluginFolderCleanup.OwnVersion(other, Id).Should().BeNull();
                PluginFolderCleanup.OwnVersion(empty, Id).Should().BeNull();
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
