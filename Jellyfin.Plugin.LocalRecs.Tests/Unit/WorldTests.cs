using System;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die „Welten“ (Marvel, Star Wars, …).
    /// </summary>
    public class WorldTests
    {
        [Fact]
        public void DefaultDefinitions_AreValid_WithUniqueIds()
        {
            var worlds = WorldDefinition.Parse(WorldDefinition.DefaultDefinitions);

            worlds.Select(w => w.Id).Should().Contain(new[] { "marvel", "star-wars", "pixar", "disney", "harry-potter", "mittelerde" });
            worlds.Select(w => w.Id).Should().OnlyHaveUniqueItems();
            worlds.Single(w => w.Id == "pixar").Collections.Should().Contain("Toy Story");
        }

        [Fact]
        public void Parse_IgnoresCommentsEmptyAndDuplicateLines()
        {
            var worlds = WorldDefinition.Parse("# Kommentar\n\nStar Wars | Lucasfilm | Star Wars\nStar Wars | X | Y\nOhne Regeln | |\nÄrzte & Co. | Bavaria |");

            worlds.Select(w => w.Id).Should().Equal("star-wars", "aerzte-co");
            worlds[0].Studios.Should().Equal("Lucasfilm");
        }

        [Fact]
        public void Matching_IsPartialAndCaseInsensitive()
        {
            var marvel = WorldDefinition.Parse("Marvel | Marvel | Avengers").Single();

            marvel.MatchesStudio(new[] { "Walt Disney Pictures", "MARVEL STUDIOS" }).Should().BeTrue();
            marvel.MatchesStudio(new[] { "Pixar" }).Should().BeFalse();
            marvel.MatchesStudio(null).Should().BeFalse();
            marvel.MatchesCollection("The Avengers Collection").Should().BeTrue();
            marvel.MatchesCollection("Toy Story Filmreihe").Should().BeFalse();
        }

        [Fact]
        public void Worlds_RecentlyWatchedFirst_ThenBySize()
        {
            var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            var ordered = WorldsController.Order(new (WorldsController.WorldInfo, DateTime?)[]
            {
                (new WorldsController.WorldInfo { Id = "disney", ItemCount = 12 }, null),
                (new WorldsController.WorldInfo { Id = "marvel", ItemCount = 58 }, now.AddDays(-20)),
                (new WorldsController.WorldInfo { Id = "pixar", ItemCount = 5 }, now.AddDays(-1)),
                (new WorldsController.WorldInfo { Id = "illumination", ItemCount = 18 }, null)
            });

            ordered.Select(w => w.Id).Should().Equal("pixar", "marvel", "illumination", "disney");
        }
    }
}
