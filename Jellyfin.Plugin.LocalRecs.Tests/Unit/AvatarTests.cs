using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Avatars;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Profilbilder: Katalog lesen, Bildquellen auflösen, Kataloge zusammenführen, Platzierung und Bildformate.
    /// </summary>
    public class AvatarTests
    {
        private const string Source = "https://example.org/icons/icons.json";

        private const string Json = @"{
            ""format"": 1, ""name"": ""Test"", ""license"": ""CC BY-SA"",
            ""colors"": [""#E50914"", ""#2e96e0"", ""rot"", ""#2E96E0""],
            ""categories"": [
                { ""id"": ""tiere"", ""name"": ""Tiere"", ""icons"": [
                    { ""id"": ""katze"", ""name"": ""Katze"", ""file"": ""tiere/katze.png"" },
                    { ""id"": ""hund"", ""url"": ""https://cdn.example.org/hund.png"", ""fit"": ""cover"" },
                    { ""id"": ""böse"", ""file"": ""x.png"" },
                    { ""id"": ""js"", ""url"": ""javascript:alert(1)"" },
                    { ""id"": ""katze"", ""file"": ""doppelt.png"" } ] },
                { ""id"": ""leer"", ""icons"": [] },
                { ""id"": ""Gross"", ""icons"": [ { ""id"": ""a"", ""file"": ""a.png"" } ] }
            ]
        }";

        [Fact]
        public void Parse_KeepsValidEntriesOnly()
        {
            var catalog = AvatarCatalog.Parse(Json, Source);

            catalog.Name.Should().Be("Test");
            catalog.Colors.Should().Equal("#e50914", "#2e96e0");
            catalog.Categories.Select(c => c.Id).Should().Equal("tiere");
            var icons = catalog.Categories[0].Icons;
            icons.Select(i => i.Id).Should().Equal("katze", "hund");
            icons[0].Name.Should().Be("Katze");
            icons[0].Source.Should().Be("https://example.org/icons/tiere/katze.png");
            icons[0].Fit.Should().Be(AvatarCatalog.FitContain);
            icons[1].Name.Should().Be("hund");
            icons[1].Fit.Should().Be(AvatarCatalog.FitCover);
        }

        [Fact]
        public void Parse_WithoutColors_UsesDefaultPalette()
        {
            var catalog = AvatarCatalog.Parse(@"{ ""categories"": [] }", Source);

            catalog.Colors.Should().NotBeEmpty();
            catalog.Colors.Should().OnlyContain(c => c.StartsWith('#') && c.Length == 7);
        }

        [Fact]
        public void FindAndPaletteColor_Work()
        {
            var catalog = AvatarCatalog.Parse(Json, Source);

            catalog.Find("tiere/hund")!.Id.Should().Be("hund");
            catalog.Find("tiere")!.Should().BeNull();
            catalog.Find("tiere/maus").Should().BeNull();
            catalog.Find(null).Should().BeNull();
            catalog.PaletteColor("#2E96E0").Should().Be("#2e96e0");
            catalog.PaletteColor("#123456").Should().BeNull();
        }

        [Fact]
        public void ResolveImage_LocalFilesStayInCatalogFolder()
        {
            var directory = Path.Combine(Path.GetTempPath(), "lr-icons");
            var catalog = Path.Combine(directory, "icons.json");

            AvatarCatalog.ResolveImage(catalog, "tiere/katze.png", null).Should().Be(Path.Combine(directory, "tiere", "katze.png"));
            AvatarCatalog.ResolveImage(catalog, "../geheim.png", null).Should().BeNull();
            AvatarCatalog.ResolveImage(Source, "../../a.png", null).Should().Be("https://example.org/a.png");
            AvatarCatalog.ResolveImage(Source, null, "ftp://example.org/a.png").Should().BeNull();
            AvatarCatalog.ResolveImage(Source, null, null).Should().BeNull();
        }

        [Fact]
        public void Merge_FirstCatalogGivesPalette_CategoriesAreAdded()
        {
            var first = AvatarCatalog.Parse(Json, Source);
            var second = AvatarCatalog.Parse(@"{ ""license"": ""Andere"", ""colors"": [""#000000""], ""categories"": [
                { ""id"": ""tiere"", ""icons"": [ { ""id"": ""maus"", ""file"": ""m.png"" } ] },
                { ""id"": ""helden"", ""icons"": [ { ""id"": ""held"", ""file"": ""h.png"" } ] } ] }", "https://other.org/icons.json");

            var merged = AvatarService.Merge(new[] { null, first, second })!;

            merged.Colors.Should().Equal("#e50914", "#2e96e0");
            merged.Categories.Select(c => c.Id).Should().Equal("tiere", "helden");
            merged.Find("tiere/maus").Should().BeNull();
            merged.Find("helden/held")!.Source.Should().Be("https://other.org/h.png");
            merged.License.Should().Be("CC BY-SA · Andere");
            AvatarService.Merge(new AvatarCatalog?[] { null }).Should().BeNull();
        }

        [Fact]
        public void Placement_ContainCentersAt80Percent_CoverFills()
        {
            var contain = AvatarRenderer.Placement(256, 128, 512, AvatarCatalog.FitContain);
            contain.Width.Should().BeApproximately(409.6f, 0.01f);
            contain.Height.Should().BeApproximately(204.8f, 0.01f);
            contain.Left.Should().BeApproximately(51.2f, 0.01f);
            contain.Top.Should().BeApproximately(153.6f, 0.01f);

            var cover = AvatarRenderer.Placement(256, 128, 512, AvatarCatalog.FitCover);
            cover.Height.Should().Be(512f);
            cover.Width.Should().Be(1024f);
            cover.Left.Should().Be(-256f);
        }

        [Fact]
        public void DetectContentType_AcceptsPngJpegWebpOnly()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
            var webp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
            var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"x\"></svg>");

            AvatarRenderer.DetectContentType(png).Should().Be("image/png");
            AvatarRenderer.DetectContentType(jpeg).Should().Be("image/jpeg");
            AvatarRenderer.DetectContentType(webp).Should().Be("image/webp");
            AvatarRenderer.DetectContentType(svg).Should().BeNull();
            AvatarRenderer.DetectContentType(new byte[3]).Should().BeNull();
        }
    }
}
