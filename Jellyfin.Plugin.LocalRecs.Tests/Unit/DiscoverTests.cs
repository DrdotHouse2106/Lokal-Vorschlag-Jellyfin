using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Discover;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Entdecken-Reihen aus TMDB.
    /// </summary>
    public class DiscoverTests
    {
        private const string MovieJson = "{\"id\":550,\"title\":\"Neuer Film\",\"original_title\":\"New Movie\",\"overview\":\"Worum es geht.\",\"poster_path\":\"/p.jpg\",\"backdrop_path\":\"/b.jpg\","
            + "\"release_date\":\"2026-10-01\",\"runtime\":118,\"popularity\":55.5,\"genres\":[{\"id\":28,\"name\":\"Action\"},{\"id\":35,\"name\":\"Komödie\"}],"
            + "\"release_dates\":{\"results\":[{\"iso_3166_1\":\"US\",\"release_dates\":[{\"type\":3,\"release_date\":\"2026-10-24T00:00:00.000Z\",\"certification\":\"R\"}]},"
            + "{\"iso_3166_1\":\"DE\",\"release_dates\":[{\"type\":1,\"release_date\":\"2026-09-10T00:00:00.000Z\",\"certification\":\"\"},{\"type\":3,\"release_date\":\"2026-11-05T00:00:00.000Z\",\"certification\":\"12\"},{\"type\":4,\"release_date\":\"2027-01-10T00:00:00.000Z\"}]}]},"
            + "\"videos\":{\"results\":[{\"site\":\"YouTube\",\"type\":\"Teaser\",\"key\":\"teaserDE\",\"iso_639_1\":\"de\"},{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"trailerEN\",\"iso_639_1\":\"en\",\"official\":true},"
            + "{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"trailerDE\",\"name\":\"Trailer (deutsch)\",\"iso_639_1\":\"de\"}]},"
            + "\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Netflix\"},{\"provider_name\":\"Netflix\"}]}}}}";

        private const string SeriesJson = "{\"id\":1399,\"name\":\"Neue Serie\",\"first_air_date\":\"2026-03-03\",\"episode_run_time\":[45],\"genres\":[{\"name\":\"Drama\"}],"
            + "\"content_ratings\":{\"results\":[{\"iso_3166_1\":\"US\",\"rating\":\"TV-MA\"},{\"iso_3166_1\":\"DE\",\"rating\":\"16\"}]},"
            + "\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Disney Plus\"}]}}}}";

        [Fact]
        public void ParseList()
        {
            TmdbDiscover.ParseList("{\"results\":[{\"id\":1,\"popularity\":3.5},{\"id\":2},{\"title\":\"ohne\"}]}")
                .Should().Equal((1, 3.5), (2, 0.0));
        }

        [Fact]
        public void ParseMovie_GermanRatingTrailerProviders()
        {
            var (item, releases) = TmdbDiscover.ParseDetails(MovieJson, false, "DE")!.Value;
            item.TmdbId.Should().Be(550);
            item.MediaType.Should().Be("movie");
            item.Title.Should().Be("Neuer Film");
            item.Year.Should().Be(2026);
            item.Genres.Should().Equal("Action", "Komödie");
            item.OfficialRating.Should().Be("12");
            item.RuntimeMinutes.Should().Be(118);
            item.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w342/p.jpg");
            item.TrailerKey.Should().Be("trailerDE", "deutscher Trailer vor deutschem Teaser und englischem Trailer");
            item.Providers.Should().Equal("Netflix");
            TmdbDiscover.CinemaStart(releases).Should().Be(new DateOnly(2026, 11, 5), "deutscher Kinostart, nicht US");
            TmdbDiscover.LocalRelease(releases).Should().Be(new DateOnly(2026, 11, 5));
        }

        [Fact]
        public void ParseSeries_RatingRuntimeProviders()
        {
            var (item, releases) = TmdbDiscover.ParseDetails(SeriesJson, true, "DE")!.Value;
            item.MediaType.Should().Be("tv");
            item.OfficialRating.Should().Be("16");
            item.RuntimeMinutes.Should().Be(45);
            item.Providers.Should().Equal("Disney Plus");
            releases.Should().ContainSingle().Which.Should().Be((0, new DateOnly(2026, 3, 3)));
        }

        [Fact]
        public void Cinema_NextWeeksSoonestFirst()
        {
            var today = new DateOnly(2026, 10, 25);
            var details = new[]
            {
                Detail(1, (3, new DateOnly(2026, 11, 5))),
                Detail(2, (3, new DateOnly(2026, 10, 26))),
                Detail(3, (3, new DateOnly(2026, 10, 1))),
                Detail(4, (3, new DateOnly(2027, 3, 1))),
                Detail(5, (2, new DateOnly(2026, 10, 25)))
            };
            var row = DiscoverService.BuildCinema(details, today);
            row.Select(i => i.TmdbId).Should().Equal(5, 2, 1);
            row[0].ReleaseText.Should().Be("Kinostart heute");
            row[1].ReleaseText.Should().Be("Kinostart morgen");
            row[2].ReleaseText.Should().Be("Kinostart am Do. 05.11.");
            row[2].ReleaseDate.Should().Be("2026-11-05");
        }

        [Fact]
        public void NewThisYear_ReleasedNewestFirstThenUpcoming()
        {
            var today = new DateOnly(2026, 10, 25);
            var details = new[]
            {
                Detail(1, (3, new DateOnly(2026, 3, 1))),
                Detail(2, (4, new DateOnly(2026, 10, 20))),
                Detail(3, (3, new DateOnly(2026, 12, 18))),
                Detail(4, (0, new DateOnly(2025, 12, 30))),
                Detail(5, (3, new DateOnly(2026, 11, 1)))
            };
            var row = DiscoverService.BuildNewThisYear(details, today, false);
            row.Select(i => i.TmdbId).Should().Equal(2, 1, 5, 3);
            row[0].ReleaseText.Should().Be("Erschienen am 20.10.");
            row[0].Released.Should().BeTrue();
            row[2].ReleaseText.Should().Be("Erscheint am So. 01.11.");
            row[3].ReleaseText.Should().Be("Erscheint am Fr. 18.12.");
        }

        [Fact]
        public void NewSeries_UsesFirstAirDate()
        {
            var today = new DateOnly(2026, 10, 25);
            var row = DiscoverService.BuildNewThisYear(new[] { Detail(7, (0, new DateOnly(2026, 10, 24))), Detail(8, (0, new DateOnly(2026, 10, 26))) }, today, true);
            row.Select(i => i.ReleaseText).Should().Equal("Gestartet gestern", "Startet morgen");
        }

        private static (DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases) Detail(int id, params (int Type, DateOnly Date)[] releases)
            => (new DiscoverItem { TmdbId = id, Title = "T" + id }, releases);
    }
}
