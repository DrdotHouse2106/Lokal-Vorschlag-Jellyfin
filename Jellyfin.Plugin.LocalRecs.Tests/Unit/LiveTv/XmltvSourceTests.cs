using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.LiveTv
{
    /// <summary>
    /// Tests für das Einlesen von Darstellern, Regie und Jahr aus einer XMLTV-Datei.
    /// </summary>
    public class XmltvSourceTests
    {
        private const string Xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE tv SYSTEM ""xmltv.dtd"">
<tv>
  <channel id=""Das.Erste.de""><display-name>Das Erste</display-name></channel>
  <programme start=""20261016070500 +0000"" stop=""20261016075000 +0000"" channel=""Das.Erste.de"">
    <title lang=""de"">Hubert ohne Staller</title>
    <sub-title>Welpenschutz</sub-title>
    <credits><director>Manuel Weiss</director><actor role=""Franz Hubert"">Christian Tramitz</actor><actor>Paul Sedlmeir</actor><writer>Anna Autorin</writer></credits>
    <date>2023</date>
    <category lang=""de"">Krimiserie</category>
    <country lang=""de"">D</country>
    <episode-num system=""xmltv_ns"">10.7/12.</episode-num>
    <episode-num system=""onscreen"">S11 E8/12</episode-num>
    <episode-num system=""imdb.com"">title/tt1234567/</episode-num>
  </programme>
  <programme start=""20261016201500 +0200"" stop=""20261016214500 +0200"" channel=""ZDF.de"">
    <title>Tatort: Der Fall</title>
    <sub-title>Teil 1</sub-title>
    <episode-num system=""xmltv_ns"">.2.</episode-num>
    <credits><actor>Axel Prahl</actor></credits>
  </programme>
  <programme start=""20261016201500 +0200"" stop=""20261016214500 +0200"" channel=""ARD.de"">
    <title>Tatort: Der Fall</title>
    <sub-title>Teil 2</sub-title>
    <credits><actor>Jan Josef Liefers</actor></credits>
  </programme>
  <programme start=""20261016220000 +0000"" channel=""X""><title>Ohne Personen</title></programme>
</tv>";

        [Fact]
        public void Parse_ReadsPeopleYearAndCountry_AndConvertsToUtc()
        {
            var index = XmltvSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Xml)));

            var hubert = XmltvSource.Find(index, new DateTime(2026, 10, 16, 7, 5, 0, DateTimeKind.Utc), "Hubert ohne Staller", "Welpenschutz");
            hubert.Should().NotBeNull();
            hubert!.Actors.Should().Equal("Christian Tramitz", "Paul Sedlmeir");
            hubert.Directors.Should().Equal("Manuel Weiss");
            hubert.People.Should().Equal("Christian Tramitz", "Paul Sedlmeir", "Manuel Weiss");
            hubert.Year.Should().Be(2023);
            hubert.Country.Should().Be("D");

            // 20:15 +0200 = 18:15 UTC
            XmltvSource.Find(index, new DateTime(2026, 10, 16, 18, 15, 0, DateTimeKind.Utc), "Tatort: Der Fall", "Teil 2")!
                .Actors.Should().Equal("Jan Josef Liefers");
            XmltvSource.Find(index, new DateTime(2026, 10, 16, 18, 15, 0, DateTimeKind.Utc), "Tatort: Der Fall", "Teil 1")!
                .Actors.Should().Equal("Axel Prahl");
        }

        [Fact]
        public void Credits_ContainRolesWritersEpisodeAndImdb()
        {
            var index = XmltvSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Xml)));
            var id = Guid.NewGuid();

            var credits = Jellyfin.Plugin.LocalRecs.Api.LiveTvCreditsController.ToCredits(
                id, XmltvSource.Find(index, new DateTime(2026, 10, 16, 7, 5, 0, DateTimeKind.Utc), "Hubert ohne Staller", null));

            credits.Matched.Should().BeTrue();
            credits.ProgramId.Should().Be(id.ToString("N"));
            credits.Actors.Select(a => (a.Name, a.Role)).Should().Equal(("Christian Tramitz", "Franz Hubert"), ("Paul Sedlmeir", (string?)null));
            credits.Directors.Should().Equal("Manuel Weiss");
            credits.Writers.Should().Equal("Anna Autorin");
            credits.Episode.Should().Be("S11 E8/12");
            credits.ImdbId.Should().Be("tt1234567");
            credits.Year.Should().Be(2023);

            XmltvSource.Find(index, new DateTime(2026, 10, 16, 18, 15, 0, DateTimeKind.Utc), "Tatort: Der Fall", "Teil 1")!.Episode.Should().Be("E3");
            Jellyfin.Plugin.LocalRecs.Api.LiveTvCreditsController.ToCredits(id, null).Matched.Should().BeFalse();
        }

        [Fact]
        public void Find_MatchesIgnoringCaseAndSeconds_ButNotOtherTimesOrTitles()
        {
            var index = XmltvSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Xml)));

            XmltvSource.Find(index, new DateTime(2026, 10, 16, 7, 5, 30, DateTimeKind.Utc), "HUBERT OHNE STALLER", null).Should().NotBeNull();
            XmltvSource.Find(index, new DateTime(2026, 10, 16, 7, 10, 0, DateTimeKind.Utc), "Hubert ohne Staller", null).Should().BeNull();
            XmltvSource.Find(index, new DateTime(2026, 10, 16, 7, 5, 0, DateTimeKind.Utc), "Die Rosenheim-Cops", null).Should().BeNull();
            XmltvSource.Find(index, new DateTime(2026, 10, 16, 22, 0, 0, DateTimeKind.Utc), "Ohne Personen", null)!.People.Should().BeEmpty();
        }

        [Fact]
        public void Parse_ReadsGzip()
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
            {
                var bytes = Encoding.UTF8.GetBytes(Xml);
                gzip.Write(bytes, 0, bytes.Length);
            }

            compressed.Position = 0;
            XmltvSource.Parse(compressed).Values.Should().HaveCount(3);
        }
    }
}
