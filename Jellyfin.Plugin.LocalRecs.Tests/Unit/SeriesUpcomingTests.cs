using System;
using System.Collections.Generic;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Upcoming;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die kommenden Folgen einer Serie (Detailseite).
    /// </summary>
    public class SeriesUpcomingTests
    {
        // Donnerstag, 22.10.2026, 10:00 Uhr in Berlin (UTC+2)
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);

        private static ArrEpisode Episode(int season, int number, DateTime airUtc, bool hasFile, string? title = null)
            => new ArrEpisode { SeasonNumber = season, EpisodeNumber = number, AirDateUtc = airUtc, HasFile = hasFile, Title = title };

        [Fact]
        public void ParseSeriesAndEpisodes()
        {
            var series = ArrClient.ParseSonarrSeries("[{\"id\":3,\"title\":\"Tulsa King\",\"tvdbId\":420,\"network\":\"Paramount+\"},{\"title\":\"ohne Id\"}]");
            series.Should().ContainSingle();
            series[0].SonarrSeriesId.Should().Be(3);
            series[0].TvdbId.Should().Be("420");
            series[0].Network.Should().Be("Paramount+");

            var episodes = ArrClient.ParseSonarrEpisodes("[{\"seriesId\":3,\"seasonNumber\":2,\"episodeNumber\":5,\"title\":\"T\",\"airDateUtc\":\"2026-10-22T19:00:00Z\",\"hasFile\":true},"
                + "{\"seriesId\":3,\"seasonNumber\":2,\"episodeNumber\":6}]");
            episodes.Should().ContainSingle();
            episodes[0].HasFile.Should().BeTrue();
        }

        [Fact]
        public void NextEpisodeToday_WithTimeNetworkAndTitle()
        {
            var result = SeriesUpcomingService.Build(
                new[] { Episode(2, 4, Now.AddDays(-7), true), Episode(2, 5, new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc), false, "Folgentitel") },
                "Paramount+",
                Now,
                120,
                new Dictionary<(int Season, int Episode), Guid>());
            result.Text.Should().Be("Neue Folge heute · 21:00 Uhr · Paramount+ · S02E05 Folgentitel");
            result.Next!.LocalDate.Should().Be("2026-10-22");
            result.MoreText.Should().BeEmpty();
            result.NewText.Should().BeEmpty();
        }

        [Fact]
        public void NextEpisodeLater_NoTimeLimit_AndMore()
        {
            var result = SeriesUpcomingService.Build(
                new[]
                {
                    Episode(3, 2, new DateTime(2027, 3, 12, 20, 0, 0, DateTimeKind.Utc), false),
                    Episode(3, 3, new DateTime(2027, 3, 19, 20, 0, 0, DateTimeKind.Utc), false),
                    Episode(3, 4, new DateTime(2027, 3, 26, 20, 0, 0, DateTimeKind.Utc), false),
                    Episode(0, 1, new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc), false)
                },
                null,
                Now,
                60,
                new Dictionary<(int Season, int Episode), Guid>());
            result.Next!.SeasonNumber.Should().Be(3);
            result.Text.Should().Be("Nächste Folge am Fr. 12.03. · 21:00 Uhr · S03E02");
            result.MoreEpisodes.Should().Be(2);
            result.MoreText.Should().Be("+2 weitere bis 26.03.");
            result.MoreUntil.Should().Be("2027-03-26");
        }

        [Fact]
        public void SeasonPremiereTomorrow()
        {
            var result = SeriesUpcomingService.Build(
                new[] { Episode(3, 1, new DateTime(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc), false), Episode(3, 2, new DateTime(2026, 10, 30, 19, 0, 0, DateTimeKind.Utc), false) },
                null,
                Now,
                120,
                new Dictionary<(int Season, int Episode), Guid>());
            result.Text.Should().StartWith("Staffelstart morgen · 21:00 Uhr · S03E01");
            result.MoreText.Should().Be("+1 weitere am 30.10.");
        }

        [Fact]
        public void AiredTodayAndImported_IsNew()
        {
            var id = Guid.NewGuid();
            var result = SeriesUpcomingService.Build(
                new[] { Episode(2, 5, new DateTime(2026, 10, 22, 6, 0, 0, DateTimeKind.Utc), true), Episode(2, 4, new DateTime(2026, 10, 15, 6, 0, 0, DateTimeKind.Utc), true) },
                null,
                Now,
                120,
                new Dictionary<(int Season, int Episode), Guid> { [(2, 5)] = id });
            result.Next.Should().BeNull();
            result.Text.Should().BeEmpty();
            result.NewText.Should().Be("Neu: S02E05");
            result.NewToday.Should().ContainSingle().Which.ItemId.Should().Be(id.ToString("N"));
        }
    }
}
