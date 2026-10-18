using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;
using Collection = Jellyfin.Plugin.LocalRecs.Worlds.WorldMembership.CollectionEntry;
using Entry = Jellyfin.Plugin.LocalRecs.Worlds.WorldMembership.LibraryEntry;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die gepflegte Welten-Liste (welten.json) und die Zuordnung der Titel.
    /// </summary>
    public class WorldListTests
    {
        private const string Json = @"{
  ""welten"": [
    { ""name"": ""Pixar"", ""studios"": [""Pixar""], ""sammlungen"": [""Toy Story""],
      ""titel"": [ { ""tmdb"": 2000, ""typ"": ""film"", ""name"": ""Film ohne Studio"" }, { ""imdb"": ""tt0000001"" } ],
      ""ausschluss"": [ { ""tmdb"": ""3000"", ""typ"": ""film"" } ] },
    { ""name"": ""Star Wars"", ""studios"": [""Lucasfilm""], ""ausschluss"": [ { ""sammlung"": ""Indiana Jones"" } ] },
    { ""name"": ""Pixar"", ""studios"": [""Doppelt""] },
    { ""name"": ""Leer"" },
  ]
}";

        private static readonly Entry ToyStory = new Entry { Id = Guid.NewGuid(), Studios = new[] { "Walt Disney Pictures" }, Tmdb = "862" };
        private static readonly Entry Cars = new Entry { Id = Guid.NewGuid(), Studios = new[] { "Pixar Animation Studios" }, Tmdb = "920" };
        private static readonly Entry NoStudio = new Entry { Id = Guid.NewGuid(), Tmdb = "2000" };
        private static readonly Entry SeriesSameTmdb = new Entry { Id = Guid.NewGuid(), IsSeries = true, Tmdb = "2000" };
        private static readonly Entry ByImdb = new Entry { Id = Guid.NewGuid(), IsSeries = true, Imdb = "TT0000001" };
        private static readonly Entry Excluded = new Entry { Id = Guid.NewGuid(), Studios = new[] { "Pixar" }, Tmdb = "3000" };
        private static readonly Entry Mandalorian = new Entry { Id = Guid.NewGuid(), IsSeries = true, Studios = new[] { "Lucasfilm Ltd." } };
        private static readonly Entry Indy = new Entry { Id = Guid.NewGuid(), Studios = new[] { "Lucasfilm Ltd.", "Paramount" } };
        private static readonly Entry ToyStory4 = new Entry { Id = Guid.NewGuid(), Studios = new[] { "Disney" }, Tmdb = "301528" };

        [Fact]
        public void ParseJson_ReadsWorlds_SkipsDuplicatesAndEmpty()
        {
            var worlds = WorldDefinition.ParseJson(Json);

            worlds.Select(w => w.Id).Should().Equal("pixar", "star-wars");
            worlds[0].Includes.Should().HaveCount(2);
            worlds[0].Includes[0].Tmdb.Should().Be("2000", "Zahlen werden als Text gelesen");
            worlds[1].Excludes.Single().Collection.Should().Be("Indiana Jones");
        }

        [Fact]
        public void ParseJson_ReadsTrailer_AsIdOrObject()
        {
            var worlds = WorldDefinition.ParseJson(@"{ ""welten"": [
  { ""name"": ""A"", ""studios"": [""A""], ""trailer"": 920 },
  { ""name"": ""B"", ""studios"": [""B""], ""trailer"": { ""imdb"": ""tt0000001"", ""typ"": ""serie"" } },
  { ""name"": ""C"", ""studios"": [""C""], ""trailer"": { ""name"": ""ohne ID"" } },
  { ""name"": ""D"", ""studios"": [""D""] } ] }");

            worlds[0].Trailer!.Tmdb.Should().Be("920");
            worlds[1].Trailer!.Imdb.Should().Be("tt0000001");
            worlds[1].Trailer!.Type.Should().Be("serie");
            worlds[2].Trailer.Should().BeNull("ein Eintrag ohne ID kann nicht zugeordnet werden");
            worlds[3].Trailer.Should().BeNull();
        }

        [Fact]
        public void Membership_ResolvesPinnedTrailerTitle()
        {
            var worlds = WorldDefinition.ParseJson(@"{ ""welten"": [
  { ""name"": ""Pixar"", ""studios"": [""Pixar""], ""trailer"": ""920"" },
  { ""name"": ""Star Wars"", ""studios"": [""Lucasfilm""], ""trailer"": ""999999"" } ] }");
            var membership = WorldMembership.Compute(worlds, new[] { Cars, Mandalorian }, Array.Empty<Collection>());

            membership.PinnedTrailers["pixar"].Should().Be(Cars.Id);
            membership.PinnedTrailers.Should().NotContainKey("star-wars", "der Titel ist nicht in der Bibliothek");
        }

        [Fact]
        public void PickForDay_RotatesDaily_StableWithinDay()
        {
            var entries = new[] { "a", "b", "c" };
            var day = new DateTime(2026, 10, 18, 8, 0, 0);

            WorldService.PickForDay(entries, day).Should().Be(WorldService.PickForDay(entries, day.AddHours(15)));
            new[] { 0, 1, 2 }.Select(d => WorldService.PickForDay(entries, day.AddDays(d))).Should().OnlyHaveUniqueItems();
            WorldService.PickForDay(Array.Empty<string>(), day).Should().BeNull();
        }

        [Fact]
        public void ParseJson_WithoutWorlds_Throws()
        {
            var act = () => WorldDefinition.ParseJson("{ \"x\": 1 }");
            act.Should().Throw<JsonException>();
        }

        [Fact]
        public void Membership_CombinesStudiosCollectionsAndList_MinusExclusions()
        {
            var membership = Compute();

            membership.Members["pixar"].Should().BeEquivalentTo(new[] { ToyStory.Id, Cars.Id, NoStudio.Id, ByImdb.Id });
            membership.Sources["pixar"][ToyStory.Id].Should().Be("Sammlung: Toy Story Filmreihe");
            membership.Sources["pixar"][Cars.Id].Should().Be("Studio: Pixar Animation Studios");
            membership.Sources["pixar"][NoStudio.Id].Should().Be("Liste");
            membership.Members["pixar"].Should().NotContain(SeriesSameTmdb.Id, "TMDB-ID gilt nur für Filme (typ)");
            membership.Members["pixar"].Should().NotContain(Excluded.Id);
        }

        [Fact]
        public void CollectionExclusion_RemovesWholeCollection()
        {
            var membership = Compute();

            membership.Members["star-wars"].Should().BeEquivalentTo(new[] { Mandalorian.Id });
        }

        [Fact]
        public void Suggestions_AreOtherPartsOfCollectionsWithWorldTitles()
        {
            var membership = Compute();

            membership.Suggest("pixar").Select(s => s.ItemId).Should().Equal(ToyStory4.Id);
            membership.TmdbCollections["pixar"].Should().Equal("10194");
        }

        [Fact]
        public void BundledListFile_IsValid()
        {
            var path = Path.Combine(FindRepoRoot(), "welten", "welten.json");
            var worlds = WorldDefinition.ParseJson(File.ReadAllText(path));

            worlds.Select(w => w.Id).Should().Contain(new[] { "marvel", "star-wars", "pixar", "studio-ghibli" });
        }

        private static WorldMembership Compute()
        {
            var worlds = WorldDefinition.ParseJson(Json);
            var items = new[] { ToyStory, Cars, NoStudio, SeriesSameTmdb, ByImdb, Excluded, Mandalorian, Indy, ToyStory4 };
            var collections = new[]
            {
                new Collection { Name = "Toy Story Filmreihe", TmdbId = "10194", ChildIds = new[] { ToyStory.Id } },
                new Collection { Name = "Cars Collection", ChildIds = new[] { Cars.Id, ToyStory4.Id } },
                new Collection { Name = "Indiana Jones Filmreihe", TmdbId = "84", ChildIds = new[] { Indy.Id } }
            };
            return WorldMembership.Compute(worlds, items, collections);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "welten")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository nicht gefunden");
        }
    }
}
