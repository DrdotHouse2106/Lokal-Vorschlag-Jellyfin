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

        [Theory]
        [InlineData("fanart:collection/1241", true, "fanart:collection/1241")]
        [InlineData("Fanart:Movie/671", true, "fanart:movie/671")]
        [InlineData("fanart:tv/81189", true, "fanart:tv/81189")]
        [InlineData("fanart:collection/1241", false, null)]
        [InlineData("fanart:collection/abc", true, null)]
        [InlineData("fanart:../../etc", true, null)]
        public void Resolve_Fanart_OnlyWithKeyAndValidId(string logo, bool hasKey, string? expected)
        {
            WorldLogoService.Resolve(logo, ListUrl, hasKey).Should().Be(expected);
        }

        [Fact]
        public void ResolveAll_KeepsOrder_SkipsUnusable()
        {
            var sources = WorldLogoService.ResolveAll(new[] { "fanart:collection/10", "http://intern/x.png", "https://image.tmdb.org/t/p/w500/a.png" }, ListUrl, false);
            sources.Should().Equal("https://image.tmdb.org/t/p/w500/a.png");
            WorldLogoService.ResolveAll(new[] { "fanart:collection/10", "https://image.tmdb.org/t/p/w500/a.png" }, ListUrl, true)
                .Should().Equal("fanart:collection/10", "https://image.tmdb.org/t/p/w500/a.png");
        }

        [Fact]
        public void PickFanartLogo_PrefersGermanThenEnglish_HdFirst_ThenLikes()
        {
            const string Json = @"{
  ""hdmovielogo"": [
    { ""url"": ""https://assets.fanart.tv/en-hd-few.png"", ""lang"": ""en"", ""likes"": ""2"" },
    { ""url"": ""https://assets.fanart.tv/en-hd-many.png"", ""lang"": ""en"", ""likes"": ""9"" },
    { ""url"": ""http://unsicher/de.png"", ""lang"": ""de"", ""likes"": ""50"" } ],
  ""movielogo"": [ { ""url"": ""https://assets.fanart.tv/de-sd.png"", ""lang"": ""de"", ""likes"": ""1"" } ] }";

            WorldLogoService.PickFanartLogo(Json).Should().Be("https://assets.fanart.tv/de-sd.png", "Deutsch zuerst, unsichere http-Adressen nie");
            WorldLogoService.PickFanartLogo(Json.Replace("\"de\"", "\"fr\"")).Should().Be("https://assets.fanart.tv/en-hd-many.png");
            WorldLogoService.PickFanartLogo("{}").Should().BeNull();
        }

        [Fact]
        public void ParseJson_ReadsLogoListAndAliases()
        {
            var world = WorldDefinition.ParseJson("{ \"welten\": [ { \"name\": \"Wizarding World\", \"aliase\": [\"harry-potter\", \"Harry Potter\"], \"sammlungen\": [\"Harry Potter\"], \"logo\": [\"fanart:collection/1241\", \"https://image.tmdb.org/x.png\"] } ] }")[0];

            world.Id.Should().Be("wizarding-world");
            world.Aliases.Should().Equal("harry-potter");
            world.HasId("harry-potter").Should().BeTrue();
            world.HasId("WIZARDING-WORLD").Should().BeTrue();
            world.HasId("marvel").Should().BeFalse();
            world.Logos.Should().Equal("fanart:collection/1241", "https://image.tmdb.org/x.png");
            world.Logo.Should().Be("fanart:collection/1241");
        }

        [Fact]
        public void ParseJson_ReadsLogo()
        {
            var worlds = WorldDefinition.ParseJson("{ \"welten\": [ { \"name\": \"Marvel\", \"studios\": [\"Marvel Studios\"], \"logo\": \" https://image.tmdb.org/x.png \" } ] }");
            worlds[0].Logo.Should().Be("https://image.tmdb.org/x.png");
        }
    }
}
