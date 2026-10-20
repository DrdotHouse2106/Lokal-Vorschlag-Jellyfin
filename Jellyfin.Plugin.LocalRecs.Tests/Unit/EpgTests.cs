using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Epg;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für den eigenen EPG-Anbieter: XMLTV lesen, Regel-Bausteine (Serie/Film/Kinder/Nachrichten/Sport) und Kennungen.
    /// </summary>
    public class EpgTests
    {
        private const string Xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE tv SYSTEM ""xmltv.dtd"">
<tv>
  <channel id=""sixx.de""><display-name>sixx</display-name><icon src=""http://logo/sixx.png""/></channel>
  <channel id=""kika.de""><display-name>KiKA</display-name><lcn>28</lcn></channel>
  <channel id=""hse.de""><display-name>HSE</display-name></channel>
  <programme start=""20261020175500 +0200"" stop=""20261020185000 +0200"" channel=""sixx.de"">
    <title lang=""de"">Ugly Betty</title>
    <sub-title lang=""de"">Die neue Chefin</sub-title>
    <desc lang=""de"">Betty hat einen neuen Job.</desc>
    <category lang=""de"">Komödie</category>
    <category lang=""de"">Serie</category>
    <episode-num system=""xmltv_ns"">1.1.</episode-num>
    <episode-num system=""onscreen"">S02E02</episode-num>
    <previously-shown/>
  </programme>
  <programme start=""20261020201500 +0200"" stop=""20261020220000 +0200"" channel=""sixx.de"">
    <title>Lautlose Morde</title>
    <desc>Thriller.</desc>
    <credits><director>Jane Doe</director><actor>John Roe</actor></credits>
    <date>2004</date>
    <category>Komödie</category>
    <url>https://www.imdb.com/title/tt0123456/</url>
    <star-rating><value>7/10</value></star-rating>
    <rating system=""FSK""><value>12</value></rating>
  </programme>
  <programme start=""20261020100000 +0200"" stop=""20261020130000 +0200"" channel=""hse.de"">
    <title>Schmuck-Highlights</title>
    <category>Shopping</category>
  </programme>
  <programme start=""20261020090000 +0200"" stop=""20261020092500 +0200"" channel=""kika.de"">
    <title>Die Sendung mit der Maus</title>
    <sub-title>Lach- und Sachgeschichten</sub-title>
    <category>Magazin</category>
  </programme>
  <programme start=""20261020060000 +0200"" stop=""20261020071500 +0200"" channel=""hse.de"">
    <title>premiumshopping.tv</title>
    <date>2026</date>
  </programme>
  <programme start=""20261020150000 +0200"" stop=""20261020170000 +0200"" channel=""sixx.de"">
    <title>Tennis: ATP Wien</title>
    <date>20261020</date>
    <category>Tennis</category>
  </programme>
</tv>";

        private static EpgFile File => EpgFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(Xml)), "de");

        private static EpgClassification Classify(string title, EpgRuleSettings? settings = null)
        {
            var file = File;
            var programme = file.Programmes.First(p => p.Title == title);
            var channel = file.Channels.First(c => c.Id == programme.ChannelId).Name;
            return EpgClassifier.Classify(programme, channel, settings ?? Defaults());
        }

        private static EpgRuleSettings Defaults() => EpgRuleSettings.From(new Configuration.PluginConfiguration());

        [Fact]
        public void Read_ParsesChannelsAndProgrammes()
        {
            var file = File;

            file.Channels.Select(c => c.Name).Should().Equal("sixx", "KiKA", "HSE");
            file.Channels[1].Number.Should().Be("28");
            file.Channels[0].IconUrl.Should().Be("http://logo/sixx.png");
            file.Programmes.Should().HaveCount(6);
            var betty = file.Programmes.First(p => p.Title == "Ugly Betty");
            betty.SubTitle.Should().Be("Die neue Chefin");
            betty.Categories.Should().Equal("Komödie", "Serie");
            betty.EpisodeNumXmltvNs.Should().Be("1.1.");
            betty.EpisodeNumOnscreen.Should().Be("S02E02");
            betty.Start.Should().Be(new DateTimeOffset(2026, 10, 20, 17, 55, 0, TimeSpan.FromHours(2)));
            betty.PreviouslyShown.Should().BeTrue();
            var movie = file.Programmes.First(p => p.Title == "Lautlose Morde");
            movie.Directors.Should().Equal("Jane Doe");
            movie.Date.Should().Be("2004");
            movie.StarRating.Should().Be(7f);
            movie.Rating.Should().Be("12");
        }

        [Theory]
        [InlineData("1.1.", 2, 2)]
        [InlineData("0.4.0/1", 1, 5)]
        [InlineData(".7.", null, 8)]
        [InlineData("3/10.", 4, null)]
        [InlineData("", null, null)]
        public void ParseXmltvNs_IsZeroBased(string value, int? season, int? episode)
        {
            EpgFile.ParseXmltvNs(value).Should().Be((season, episode));
        }

        [Theory]
        [InlineData("S02E02", 2, 2)]
        [InlineData("S1 E12", 1, 12)]
        [InlineData("Staffel 3, Folge 7", 3, 7)]
        [InlineData("2x05", 2, 5)]
        [InlineData("Folge 12", null, 12)]
        [InlineData("Teil 2", null, null)]
        public void ParseOnscreen_UnderstandsCommonFormats(string value, int? season, int? episode)
        {
            EpgFile.ParseOnscreen(value).Should().Be((season, episode));
        }

        [Fact]
        public void SeriesWithEpisodeNumber_StaysSeries_EvenWithComedyCategory()
        {
            var c = Classify("Ugly Betty");

            c.IsSeries.Should().BeTrue();
            c.IsMovie.Should().BeFalse();
            c.Season.Should().Be(2);
            c.Episode.Should().Be(2);
            c.EpisodeTitle.Should().Be("Die neue Chefin");
        }

        [Fact]
        public void LongProgrammeWithDirector_IsMovie_WithYearAndImdb()
        {
            var c = Classify("Lautlose Morde");

            c.IsMovie.Should().BeTrue();
            c.IsSeries.Should().BeFalse();
            c.Year.Should().Be(2004);
            c.ImdbId.Should().Be("tt0123456");
            c.Reasons.Should().Contain(r => r.StartsWith("Film:", StringComparison.Ordinal));
        }

        [Fact]
        public void LongProgrammeWithoutEvidence_IsNoMovie()
        {
            var settings = Defaults();
            settings.OtherChannels = Array.Empty<string>();
            settings.OtherCategories = Array.Empty<string>();
            var c = Classify("Schmuck-Highlights", settings);

            c.IsMovie.Should().BeFalse();
            c.IsSeries.Should().BeFalse();
            c.Reasons.Should().Contain(r => r.Contains("ohne Regie", StringComparison.Ordinal));
        }

        [Fact]
        public void Sports_IsNeverMovie_EvenWithDate()
        {
            var c = Classify("Tennis: ATP Wien");

            c.IsSports.Should().BeTrue();
            c.IsMovie.Should().BeFalse();
        }

        [Fact]
        public void KidsChannel_MakesKids_SubTitleMakesSeries()
        {
            var c = Classify("Die Sendung mit der Maus");

            c.IsKids.Should().BeTrue();
            c.IsSeries.Should().BeTrue();
            c.EpisodeTitle.Should().Be("Lach- und Sachgeschichten");
        }

        [Fact]
        public void Rules_CanBeSwitchedOff()
        {
            var settings = Defaults();
            settings.SeriesByEpisodeNumber = false;
            settings.MovieByDirector = false;
            settings.MovieByImdb = false;

            Classify("Ugly Betty", settings).Season.Should().BeNull();
            Classify("Lautlose Morde", settings).IsMovie.Should().BeFalse();
            settings.MovieMinMinutes = 200;
            settings.MovieByDirector = true;
            Classify("Lautlose Morde", settings).IsMovie.Should().BeFalse();
        }

        [Fact]
        public void Mapper_UsesJellyfinIds_AndKeepsGenres()
        {
            var file = File;
            var betty = file.Programmes.First(p => p.Title == "Ugly Betty");
            var info = EpgMapper.ToProgramInfo(betty, EpgClassifier.Classify(betty, "sixx", Defaults()));

            info.Id.Should().Be("sixx.de_2026-10-20T17:55:00.0000000+02:00");
            info.StartDate.Should().Be(new DateTime(2026, 10, 20, 15, 55, 0, DateTimeKind.Utc));
            info.IsSeries.Should().BeTrue();
            info.IsMovie.Should().BeFalse();
            info.SeasonNumber.Should().Be(2);
            info.EpisodeNumber.Should().Be(2);
            info.EpisodeTitle.Should().Be("Die neue Chefin");
            info.Genres.Should().Equal("Komödie", "Serie");
            info.IsRepeat.Should().BeTrue();
            info.SeriesId.Should().HaveLength(32);
            info.Etag.Should().StartWith(EpgMapper.EtagPrefix);

            var movie = file.Programmes.First(p => p.Title == "Lautlose Morde");
            var movieInfo = EpgMapper.ToProgramInfo(movie, EpgClassifier.Classify(movie, "sixx", Defaults()));
            movieInfo.IsMovie.Should().BeTrue();
            movieInfo.ProductionYear.Should().Be(2004);
            movieInfo.ProviderIds["Imdb"].Should().Be("tt0123456");
            movieInfo.SeriesId.Should().BeNull();
            movieInfo.Etag.Should().NotBe(info.Etag);
        }

        [Fact]
        public void YearAlone_IsNoMovie_UnlessSwitchedOn()
        {
            var settings = Defaults();
            settings.OtherChannels = Array.Empty<string>();
            var c = Classify("premiumshopping.tv", settings);
            c.IsMovie.Should().BeFalse();
            c.Year.Should().Be(2026);

            settings.MovieByYear = true;
            Classify("premiumshopping.tv", settings).IsMovie.Should().BeTrue();
        }

        [Fact]
        public void DirectorFromSecondSource_MakesMovie()
        {
            var programme = new EpgProgramme
            {
                ChannelId = "ZDF",
                Title = "Lautlose Morde",
                Start = new DateTimeOffset(2026, 10, 20, 20, 15, 0, TimeSpan.FromHours(2)),
                Stop = new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.FromHours(2))
            };
            programme.Categories.Add("Thriller");

            EpgClassifier.Classify(programme, "ZDF", Defaults()).IsMovie.Should().BeFalse();

            var extra = new EpgExtra { Year = 2004, ImdbId = "tt0123456" };
            extra.Directors.Add("Jane Doe");
            var c = EpgClassifier.Classify(programme, "ZDF", Defaults(), extra);
            c.IsMovie.Should().BeTrue();
            c.Year.Should().Be(2004);
            c.ImdbId.Should().Be("tt0123456");
            c.Reasons.Should().Contain(r => r.Contains("Darsteller-Quelle", StringComparison.Ordinal));
        }

        [Fact]
        public void Documentary_WithDirectorAndLength_IsNoMovie()
        {
            var programme = new EpgProgramme
            {
                ChannelId = "3sat",
                Title = "50 Gründe, die Emilia-Romagna zu lieben",
                Start = new DateTimeOffset(2026, 10, 20, 20, 15, 0, TimeSpan.Zero),
                Stop = new DateTimeOffset(2026, 10, 20, 21, 45, 0, TimeSpan.Zero)
            };
            programme.Categories.Add("Dokumentation");
            programme.Directors.Add("Jane Doe");

            var c = EpgClassifier.Classify(programme, "3sat", Defaults());

            c.IsMovie.Should().BeFalse();
            c.Reasons.Should().Contain("kein Film: Kategorie „Dokumentation“");
        }

        [Fact]
        public void ShoppingChannel_IsNeitherMovieNorSeries()
        {
            var programme = new EpgProgramme
            {
                ChannelId = "123tv",
                Title = "mediform",
                SubTitle = "Gesundheit & Wohlbefinden",
                EpisodeNumOnscreen = "Folge 12",
                Start = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
                Stop = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)
            };

            var c = EpgClassifier.Classify(programme, "1-2-3.tv", Defaults());

            c.IsSeries.Should().BeFalse();
            c.IsMovie.Should().BeFalse();
            c.Reasons.Should().ContainSingle(r => r.StartsWith("Sonstiges", StringComparison.Ordinal));
        }

        [Fact]
        public void ShoppingChannelRule_KeepsTeleshoppingOutOfMoviesAndSeries()
        {
            var c = Classify("premiumshopping.tv");

            c.IsMovie.Should().BeFalse();
            c.Reasons.Should().Contain("Sonstiges: Sender „HSE“");
        }

        [Fact]
        public void Sports_WithSubTitle_IsNoSeries()
        {
            var programme = new EpgProgramme
            {
                ChannelId = "dazn",
                Title = "UEFA Champions League",
                SubTitle = "Highlights",
                Start = new DateTimeOffset(2026, 10, 20, 23, 0, 0, TimeSpan.Zero),
                Stop = new DateTimeOffset(2026, 10, 20, 23, 45, 0, TimeSpan.Zero)
            };
            programme.Categories.Add("Fußball");

            var c = EpgClassifier.Classify(programme, "DAZN 1", Defaults());

            c.IsSports.Should().BeTrue();
            c.IsSeries.Should().BeFalse();
        }
    }
}
