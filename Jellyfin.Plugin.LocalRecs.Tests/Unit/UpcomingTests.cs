using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Upcoming;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für „Demnächst“ (Sonarr/Radarr).
    /// </summary>
    public class UpcomingTests
    {
        private const string SonarrJson = "[{\"seriesId\":7,\"seasonNumber\":2,\"episodeNumber\":5,\"title\":\"Folge\",\"airDateUtc\":\"2026-10-24T01:00:00Z\",\"hasFile\":false,\"overview\":\"Es geht weiter.\","
            + "\"series\":{\"id\":7,\"title\":\"Testserie\",\"tvdbId\":123,\"tmdbId\":0,\"imdbId\":\"tt1\",\"network\":\"Paramount+\"}},"
            + "{\"seriesId\":7,\"seasonNumber\":2,\"episodeNumber\":4,\"airDateUtc\":\"2026-10-20T01:00:00Z\",\"hasFile\":true,\"series\":{\"id\":7,\"title\":\"Testserie\"}},"
            + "{\"seriesId\":8,\"seasonNumber\":1,\"episodeNumber\":1,\"hasFile\":false,\"series\":{\"id\":8,\"title\":\"Ohne Datum\"}}]";

        private const string RadarrJson = "[{\"id\":42,\"title\":\"Neuer Film\",\"year\":2026,\"tmdbId\":555,\"imdbId\":\"tt5\",\"certification\":\"12\",\"rootFolderPath\":\"/data/Filme/\","
            + "\"inCinemas\":\"2026-10-30T00:00:00Z\",\"digitalRelease\":\"2026-11-14T00:00:00Z\",\"physicalRelease\":\"2026-12-01T00:00:00Z\",\"hasFile\":false,\"monitored\":true,"
            + "\"images\":[{\"coverType\":\"poster\"},{\"coverType\":\"fanart\"}]},"
            + "{\"id\":43,\"title\":\"Schon da\",\"hasFile\":true,\"monitored\":true},"
            + "{\"id\":44,\"title\":\"Nicht vorgemerkt\",\"hasFile\":false,\"monitored\":false}]";

        [Fact]
        public void ParseSonarr_SkipsDownloadedAndUndated()
        {
            var episodes = ArrClient.ParseSonarrCalendar(SonarrJson);
            episodes.Should().ContainSingle();
            var e = episodes[0];
            e.SeriesTitle.Should().Be("Testserie");
            e.TvdbId.Should().Be("123");
            e.TmdbId.Should().BeNull();
            e.ImdbId.Should().Be("tt1");
            e.SeasonNumber.Should().Be(2);
            e.EpisodeNumber.Should().Be(5);
            e.AirDateUtc.Should().Be(new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc));
            e.AirDateUtc.Kind.Should().Be(DateTimeKind.Utc);
            e.Overview.Should().Be("Es geht weiter.");
            e.Network.Should().Be("Paramount+");
        }

        [Fact]
        public void ParseRadarr_OnlyMonitoredMissing()
        {
            var movies = ArrClient.ParseRadarrCalendar(RadarrJson);
            movies.Should().ContainSingle();
            var m = movies[0];
            m.RadarrId.Should().Be(42);
            m.TmdbId.Should().Be("555");
            m.Certification.Should().Be("12");
            m.HasPoster.Should().BeTrue();
            m.HasFanart.Should().BeTrue();
            m.DigitalRelease.Should().Be(new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public void Release_EarliestInWindowWithType()
        {
            var m = ArrClient.ParseRadarrCalendar(RadarrJson)[0];
            m.InCinemas.Should().Be(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));
            UpcomingService.Release(m, new DateTime(2026, 10, 23), new DateTime(2026, 11, 30)).Should().Be((new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), UpcomingEntry.Cinema));
            UpcomingService.Release(m, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)).Should().Be((new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), UpcomingEntry.Digital));
            UpcomingService.Release(m, new DateTime(2026, 11, 20), new DateTime(2026, 12, 30)).Should().Be((new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), UpcomingEntry.Physical));
            UpcomingService.Release(m, new DateTime(2026, 12, 2), new DateTime(2026, 12, 30)).Should().BeNull();
        }

        [Theory]
        [InlineData(2026, 10, 23, "heute")]
        [InlineData(2026, 10, 24, "morgen")]
        [InlineData(2026, 10, 26, "Mo. 26.10.")]
        [InlineData(2026, 11, 14, "Sa. 14.11.")]
        public void DayText(int y, int m, int d, string expected)
        {
            UpcomingService.DayText(new DateOnly(y, m, d), new DateOnly(2026, 10, 23)).Should().Be(expected);
        }

        [Theory]
        [InlineData(1, 1, UpcomingEntry.SeriesPremiere)]
        [InlineData(3, 1, UpcomingEntry.SeasonPremiere)]
        [InlineData(3, 2, UpcomingEntry.NewEpisode)]
        [InlineData(0, 1, UpcomingEntry.NewEpisode)]
        public void EpisodeType(int season, int episode, string expected)
        {
            UpcomingService.EpisodeType(season, episode).Should().Be(expected);
        }

        [Theory]
        [InlineData(UpcomingEntry.NewEpisode, 24, "Neue Folge am Sa. 24.10.")]
        [InlineData(UpcomingEntry.NewEpisode, 22, "Neue Folge heute")]
        [InlineData(UpcomingEntry.SeasonPremiere, 23, "Staffelstart morgen")]
        [InlineData(UpcomingEntry.SeriesPremiere, 26, "Serienstart am Mo. 26.10.")]
        [InlineData(UpcomingEntry.Cinema, 29, "Kinostart am Do. 29.10.")]
        [InlineData(UpcomingEntry.Digital, 23, "Digital ab morgen")]
        [InlineData(UpcomingEntry.Physical, 30, "Auf Disc ab Fr. 30.10.")]
        public void Label(string type, int day, string expected)
        {
            UpcomingService.Label(type, new DateOnly(2026, 10, day), new DateOnly(2026, 10, 22)).Should().Be(expected);
        }

        [Theory]
        [InlineData("/data/Filme/", "filme")]
        [InlineData("D:\\Medien\\Kinder", "kinder")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void FolderName(string? path, string expected)
        {
            UpcomingService.FolderName(path).Should().Be(expected);
        }

        [Fact]
        public void MovieAllowed_ByLibraryFolder()
        {
            var all = new HashSet<string> { "filme", "kinder" };
            var kids = new HashSet<string> { "kinder" };
            UpcomingService.MovieAllowed("/movies/kinder", kids, all, false).Should().BeTrue();
            UpcomingService.MovieAllowed("/movies/filme", kids, all, false).Should().BeFalse();
            UpcomingService.MovieAllowed("/movies/unbekannt", kids, all, false).Should().BeFalse();
            UpcomingService.MovieAllowed("/movies/unbekannt", all, all, true).Should().BeTrue();
        }

        [Theory]
        [InlineData("http://192.168.1.2:8989/", "http://192.168.1.2:8989")]
        [InlineData("https://sonarr.example/sonarr", "https://sonarr.example/sonarr")]
        [InlineData("http://user:pw@host:8989", null)]
        [InlineData("ftp://host", null)]
        [InlineData("sonarr:8989", null)]
        [InlineData("", null)]
        public void BaseUrl(string url, string? expected)
        {
            ArrClient.BaseUrl(url).Should().Be(expected);
        }

        [Fact]
        public async Task Client_SendsKeyOnlyInHeader()
        {
            HttpRequestMessage? seen = null;
            var client = new ArrClient((request, _) =>
            {
                seen = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SonarrJson) });
            });
            var episodes = await client.GetEpisodesAsync("http://sonarr:8989", "geheim", new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), default);
            episodes.Should().ContainSingle();
            seen!.RequestUri!.ToString().Should().NotContain("geheim").And.Contain("includeSeries=true");
            seen.Headers.GetValues("X-Api-Key").Single().Should().Be("geheim");
        }

        [Fact]
        public async Task Client_ErrorWithoutAddress()
        {
            var client = new ArrClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            var act = () => client.GetMoviesAsync("http://radarr:7878", "geheim", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), default);
            var ex = await act.Should().ThrowAsync<HttpRequestException>();
            ex.Which.Message.Should().Be("HTTP 401");
            ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
