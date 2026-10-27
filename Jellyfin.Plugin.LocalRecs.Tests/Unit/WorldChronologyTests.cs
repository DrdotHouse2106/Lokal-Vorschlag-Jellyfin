using System;
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
        public void Compute_MapsToLibraryItems_SeriesMayRepeat_MissingTitlesLeftOut()
        {
            var worlds = WorldDefinition.ParseJson(Json);
            var enterprise = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "314" };
            var discovery = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "67198" };
            var kelvin = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = false, Tmdb = "13475" };
            var wrongType = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), IsSeries = false, Tmdb = "314" };
            var membership = WorldMembership.Compute(worlds, new[] { wrongType, enterprise, discovery, kelvin }, Array.Empty<WorldMembership.CollectionEntry>());

            var chronology = membership.Chronologies[worlds[0].Id];
            chronology.Select(c => c.Section ?? c.Label).Should().Equal("Hauptzeitlinie", "2151", "2256–2258", "Kelvin-Zeitlinie (alternativ)", "2258", "3188");
            chronology[1].ItemId.Should().Be(enterprise.Id, "der Typ muss passen (Serie, nicht der Film mit derselben TMDB-ID)");
            chronology[2].ItemId.Should().Be(discovery.Id);
            chronology[5].ItemId.Should().Be(discovery.Id);
            chronology[5].Seasons.Should().Equal(3, 4, 5);
            chronology[4].Seasons.Should().BeEmpty("Filme haben keine Staffeln");
            membership.Chronologies.Should().NotContainKey(worlds[1].Id);
        }
    }
}
