using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Reihenfolge nach Handlungszeit („chronologie“) einer Welt.
    /// </summary>
    public class WorldChronologyTests
    {
        private const string Json = "{\"welten\":[{\"name\":\"Star Trek\",\"sammlungen\":[\"Star Trek\"],"
            + "\"titel\":[{\"tmdb\":\"314\",\"typ\":\"serie\"}],"
            + "\"chronologie\":[{\"abschnitt\":\"Hauptzeitlinie\"},{\"tmdb\":\"314\",\"typ\":\"serie\",\"label\":\"2151\"},"
            + "{\"tmdb\":\"67198\",\"typ\":\"serie\",\"label\":\"2256–2258\",\"staffeln\":[1,2]},"
            + "{\"abschnitt\":\"Kelvin-Zeitlinie (alternativ)\"},{\"tmdb\":\"13475\",\"typ\":\"film\",\"label\":\"2258\"},"
            + "{\"tmdb\":\"999999\",\"typ\":\"film\",\"label\":\"nicht vorhanden\"},"
            + "{\"tmdb\":\"67198\",\"typ\":\"serie\",\"label\":\"3188\",\"staffeln\":[3,4,5]},{\"label\":\"ohne ID\"}]},"
            + "{\"name\":\"Pixar\",\"studios\":[\"Pixar\"]}]}";

        [Fact]
        public void Parse_SectionsTitlesSeasons()
        {
            var worlds = WorldDefinition.ParseJson(Json);
            var chronology = worlds[0].Chronology;
            chronology.Should().HaveCount(7, "der Eintrag ohne ID fällt weg");
            chronology[0].Section.Should().Be("Hauptzeitlinie");
            chronology[2].Title!.Tmdb.Should().Be("67198");
            chronology[2].Seasons.Should().Equal(1, 2);
            chronology[2].Label.Should().Be("2256–2258");
            worlds[1].Chronology.Should().BeEmpty();
        }

        [Fact]
        public void Compute_MapsToLibraryItems_SeriesMayRepeat_MissingTitlesKeepTheirPlace()
        {
            var worlds = WorldDefinition.ParseJson(Json);
            var enterprise = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "314" };
            var discovery = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "67198" };
            var kelvin = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = false, Tmdb = "13475" };
            var wrongType = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = false, Tmdb = "314" };
            var membership = WorldMembership.Compute(worlds, new[] { wrongType, enterprise, discovery, kelvin }, Array.Empty<WorldMembership.CollectionEntry>());

            var chronology = membership.Chronologies[worlds[0].Id];
            chronology.Select(c => c.Section ?? c.Label).Should().Equal("Hauptzeitlinie", "2151", "2256–2258", "Kelvin-Zeitlinie (alternativ)", "2258", "nicht vorhanden", "3188");
            chronology[1].ItemId.Should().Be(enterprise.Id, "der Typ muss passen (Serie, nicht der Film mit derselben TMDB-ID)");
            chronology[2].ItemId.Should().Be(discovery.Id);
            chronology[5].ItemId.Should().BeNull("fehlt in der Bibliothek");
            chronology[5].Missing.Should().Be(new WorldMembership.MissingTitle(999999, false));
            chronology[6].ItemId.Should().Be(discovery.Id);
            chronology[6].Seasons.Should().Equal(3, 4, 5);
            chronology[4].Seasons.Should().BeEmpty("Filme haben keine Staffeln");
            membership.Chronologies.Should().NotContainKey(worlds[1].Id);
                }

        [Fact]
        public void Missing_OnlyListTitlesNotInLibrary_WithTmdbAndType_NotExcluded()
        {
            const string json = "{\"welten\":[{\"name\":\"Star Trek\","
                + "\"titel\":[{\"tmdb\":\"314\",\"typ\":\"serie\"},{\"tmdb\":\"152\",\"typ\":\"film\"},{\"tmdb\":\"67198\",\"typ\":\"serie\"},"
                + "{\"tmdb\":\"500\",\"typ\":\"film\"},{\"imdb\":\"tt0000001\",\"typ\":\"film\"},{\"tmdb\":\"777\"}],"
                + "\"ausschluss\":[{\"tmdb\":\"500\",\"typ\":\"film\"}]}]}";
            var worlds = WorldDefinition.ParseJson(json);
            var enterprise = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "314" };
            var membership = WorldMembership.Compute(worlds, new[] { enterprise }, Array.Empty<WorldMembership.CollectionEntry>());

            membership.Missing[worlds[0].Id].Should().Equal(
                new WorldMembership.MissingTitle(152, false),
                new WorldMembership.MissingTitle(67198, true));
        }

        [Theory]
        [InlineData("Untitled X-Men Film", "movie", "2028-05-05", 120, false)]
        [InlineData("untitled Shang-Chi Sequel", "movie", null, null, false)]
        [InlineData("Neuer Film ohne Termin", "movie", null, null, false)]
        [InlineData("Lamp Life", "movie", "2020-01-31", 7, false)]
        [InlineData("Bambi", "movie", "1942-08-13", 70, true)]
        [InlineData("Star Wars: Ewoks", "tv", "1985-09-07", 22, true)]
        [InlineData("Neuer Kinofilm", "movie", "2027-03-01", null, true)]
        public void IsRequestable_NoPlaceholdersDatelessOrShorts(string title, string type, string? date, int? runtime, bool expected)
        {
            var item = new Jellyfin.Plugin.LocalRecs.Discover.DiscoverItem { Title = title, MediaType = type, ReleaseDate = date, RuntimeMinutes = runtime };
            Jellyfin.Plugin.LocalRecs.Api.WorldsController.IsRequestable(item).Should().Be(expected);
        }

        [Theory]
        [InlineData(false, "2026-12-17", null, "2026-11-04", false, "Kinostart am Do. 17.12.")]
        [InlineData(false, "1979-12-07", null, "2026-11-04", true, "Erschienen 1979")]
        [InlineData(false, "2026-09-12", null, "2026-11-04", true, "Erschienen am 12.09.")]
        [InlineData(true, null, "1987-09-28", "2026-11-04", true, "Serie seit 1987")]
        [InlineData(true, null, "2027-01-15", "2026-11-04", false, "Startet am Fr. 15.01.")]
        public void ApplyRelease_TextsForSingleTitles(bool series, string? cinema, string? firstAir, string today, bool released, string text)
        {
            var releases = new List<(int Type, DateOnly Date)>();
            if (cinema != null)
            {
                releases.Add((3, DateOnly.Parse(cinema, System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (firstAir != null)
            {
                releases.Add((0, DateOnly.Parse(firstAir, System.Globalization.CultureInfo.InvariantCulture)));
            }

            var item = new Jellyfin.Plugin.LocalRecs.Discover.DiscoverItem { MediaType = series ? "tv" : "movie" };
            Jellyfin.Plugin.LocalRecs.Discover.DiscoverService.ApplyRelease(item, releases, series, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));
            item.Released.Should().Be(released);
            item.ReleaseText.Should().Be(text);
        }
    }
}
