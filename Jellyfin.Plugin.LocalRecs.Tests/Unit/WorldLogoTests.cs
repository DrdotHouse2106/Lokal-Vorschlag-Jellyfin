using System.IO;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Logo-Adressen der Welten (nur https bzw. Bilder neben der Liste).
    /// </summary>
    public class WorldLogoTests
    {
        private const string ListUrl = "https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Welten/main/welten.json";

        [Theory]
        [InlineData("https://image.tmdb.org/t/p/w500/hUzeosd33nzE5MCNsZxCGEKTXaQ.png", "https://image.tmdb.org/t/p/w500/hUzeosd33nzE5MCNsZxCGEKTXaQ.png")]
        [InlineData("logos/marvel.png", "https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Welten/main/logos/marvel.png")]
        [InlineData("http://192.168.1.10/intern.png", null)]
        [InlineData("file:///etc/passwd", null)]
        [InlineData("/etc/passwd", null)]
        [InlineData("", null)]
        public void Resolve_WithUrlList_OnlyHttps(string logo, string? expected)
        {
            WorldLogoService.Resolve(logo, ListUrl).Should().Be(expected);
        }

        [Fact]
        public void Resolve_WithLocalList_OnlyImagesInsideItsFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "welten-test");
            var list = Path.Combine(folder, "welten.json");

            WorldLogoService.Resolve("logos/marvel.png", list).Should().Be(Path.Combine(folder, "logos", "marvel.png"));
            WorldLogoService.Resolve("../geheim.png", list).Should().BeNull();
            WorldLogoService.Resolve("logos/notiz.txt", list).Should().BeNull();
            WorldLogoService.Resolve("logos/marvel.png", null).Should().BeNull();
        }

        [Fact]
        public void ParseJson_ReadsLogo()
        {
            var worlds = WorldDefinition.ParseJson("{ \"welten\": [ { \"name\": \"Marvel\", \"studios\": [\"Marvel Studios\"], \"logo\": \" https://image.tmdb.org/x.png \" } ] }");
            worlds[0].Logo.Should().Be("https://image.tmdb.org/x.png");
        }
    }
}
