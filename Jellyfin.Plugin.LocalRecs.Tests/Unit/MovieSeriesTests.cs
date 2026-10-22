using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Collections;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für „Filmreihen für dich“.
    /// </summary>
    public class MovieSeriesTests
    {
        [Theory]
        [InlineData(new[] { true, true, false, false }, 2)]
        [InlineData(new[] { true, false, true, false }, 3)]
        [InlineData(new[] { false, true, true }, 0)]
        [InlineData(new[] { false, false }, 0)]
        [InlineData(new[] { true, true }, -1)]
        public void NextIndex_AfterLastWatched_OtherwiseFirstGap(bool[] played, int expected)
        {
            MovieSeriesService.NextIndex(played).Should().Be(expected);
        }

        [Fact]
        public void Order_StartedFirst_ByLastPlayed_ThenTaste_WorldsLast_FinishedOut()
        {
            var now = DateTime.UtcNow;
            var list = new[]
            {
                Series("Neu passend", started: false, taste: 0.9),
                Series("Neu weniger", started: false, taste: 0.2),
                Series("Angefangen alt", started: true, lastPlayed: now.AddDays(-30)),
                Series("Angefangen neu", started: true, lastPlayed: now.AddDays(-1)),
                Series("Welt angefangen", started: true, lastPlayed: now, inWorld: true),
                Series("Welt neu", started: false, taste: 1, inWorld: true),
                Series("Fertig", started: true, lastPlayed: now, next: -1)
            };

            MovieSeriesService.Order(list).Select(s => s.Name).Should().Equal(
                "Angefangen neu", "Angefangen alt", "Welt angefangen", "Neu passend", "Neu weniger", "Welt neu");
        }

        [Fact]
        public void Reason_NamesThePartToContinueWith()
        {
            Series("x", started: true, lastPlayed: DateTime.UtcNow, next: 2).Reason.Should().Be("Weiter mit Teil 3");
            Series("x", started: false, taste: 0).Reason.Should().Be("3 Teile");
        }

        [Fact]
        public void ParseMovie_ReadsTheCollection()
        {
            var series = MovieSeriesLookup.ParseMovie("{\"id\":245891,\"belongs_to_collection\":{\"id\":404609,\"name\":\"John Wick Filmreihe\",\"poster_path\":\"/p.jpg\",\"backdrop_path\":\"/b.jpg\"}}");

            series!.Id.Should().Be("404609");
            series.Name.Should().Be("John Wick Filmreihe");
            series.PosterPath.Should().Be("/p.jpg");
            MovieSeriesLookup.ParseMovie("{\"id\":1,\"belongs_to_collection\":null}").Should().BeNull();
        }

        [Fact]
        public async Task Refresh_SendsOnlyTheMovieId_CachesNoneAndSeries()
        {
            var requests = new List<string>();
            var lookup = new MovieSeriesLookup(
                NullLogger<MovieSeriesLookup>.Instance,
                Path.Combine(Path.GetTempPath(), "lr-reihen-" + Guid.NewGuid().ToString("N") + ".json"),
                (request, _) =>
                {
                    requests.Add(request.RequestUri!.ToString());
                    var body = request.RequestUri!.AbsolutePath.EndsWith("/603", StringComparison.Ordinal)
                        ? "{\"belongs_to_collection\":{\"id\":2344,\"name\":\"Matrix Filmreihe\"}}"
                        : "{\"belongs_to_collection\":null}";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
                },
                (_, _) => Task.CompletedTask);

            var done = await lookup.RefreshAsync(new[] { "603", "550", "603", "abc" }, new PluginConfiguration { TmdbApiKey = "k" }, "de-DE", CancellationToken.None);
            var again = await lookup.RefreshAsync(new[] { "603", "550" }, new PluginConfiguration { TmdbApiKey = "k" }, "de-DE", CancellationToken.None);

            done.Should().Be(2);
            again.Should().Be(0, "beides ist zwischengespeichert");
            requests.Should().HaveCount(2).And.OnlyContain(u => u.Contains("language=de-DE") && u.Contains("api_key=k"));
            lookup.SeriesOf("603").Should().Be("2344");
            lookup.SeriesOf("550").Should().BeNull();
            lookup.GetSeries("2344")!.Name.Should().Be("Matrix Filmreihe");
        }

        [Fact]
        public async Task Image_OnlyForKnownSeriesWithSafePath()
        {
            var lookup = new MovieSeriesLookup(
                NullLogger<MovieSeriesLookup>.Instance,
                Path.Combine(Path.GetTempPath(), "lr-reihen-" + Guid.NewGuid().ToString("N") + ".json"),
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) }),
                (_, _) => Task.CompletedTask);
            lookup.Store("1", new MovieSeriesLookup.SeriesInfo { Id = "10", Name = "A", PosterPath = "/a.jpg", BackdropPath = "/../x.jpg" }, DateTime.UtcNow);

            (await lookup.GetImageAsync("10", false, CancellationToken.None))!.Value.Data.Should().Equal(1, 2, 3);
            (await lookup.GetImageAsync("10", true, CancellationToken.None)).Should().BeNull("Pfad mit ..");
            (await lookup.GetImageAsync("99", false, CancellationToken.None)).Should().BeNull("unbekannte Reihe");
        }

        private static MovieSeriesService.MovieSeries Series(string name, bool started, double taste = 0, DateTime lastPlayed = default, bool inWorld = false, int next = 1)
        {
            return new MovieSeriesService.MovieSeries
            {
                Id = name,
                Name = name,
                Parts = new MediaBrowser.Controller.Entities.BaseItem[] { new MediaBrowser.Controller.Entities.Movies.Movie(), new MediaBrowser.Controller.Entities.Movies.Movie(), new MediaBrowser.Controller.Entities.Movies.Movie() },
                Played = new[] { started, false, false },
                NextIndex = next,
                Started = started,
                Taste = taste,
                LastPlayed = lastPlayed,
                InWorld = inWorld
            };
        }
    }
}
