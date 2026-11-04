using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Discover;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die persönliche Reihenfolge fehlender Welt-Titel und die Abschnitte der Welten.
    /// </summary>
    public class MissingRankerTests
    {
        private static readonly DateOnly Today = new DateOnly(2026, 11, 5);

        [Fact]
        public void Rank_TasteSeenSeriesAndSearchBeforeOldTitles()
        {
            var profile = new MissingRanker.TasteProfile(
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Horror"] = 1.0, ["Thriller"] = 0.6, ["Musical"] = 0.05 },
                new Dictionary<int, string> { [91361] = "Halloween" },
                new[] { "conjuring" });
            var items = new[]
            {
                Item(1, "Schneewittchen", 1937, new[] { "Musical", "Familie" }, votes: 7000, average: 7.1, popularity: 30),
                Item(2, "Halloween II", 1981, new[] { "Horror" }, votes: 1500, average: 6.5, popularity: 20, collection: 91361),
                Item(3, "Conjuring 4", 2025, new[] { "Horror", "Thriller" }, votes: 900, average: 6.8, popularity: 80),
                Item(4, "Unbekannter Horror", 1988, new[] { "Horror" }, votes: 30, average: 5.2, popularity: 1)
            };

            var ranked = MissingRanker.Rank(items, profile, Today);
            ranked.Select(i => i.TmdbId).Should().Equal(3, 2, 4, 1);
            ranked[0].Reason.Should().Be("Du hast nach „conjuring“ gesucht");
            ranked[1].Reason.Should().Be("Weil du „Halloween“ gesehen hast");
            ranked.Last().MatchScore.Should().BeLessThan(ranked.First().MatchScore!.Value);
        }

        [Fact]
        public void Rank_GenreReason_AndNoTasteStillOrdersByQuality()
        {
            var taste = new MissingRanker.TasteProfile(new Dictionary<string, double> { ["Horror"] = 1.0, ["Thriller"] = 0.8 }, new Dictionary<int, string>(), Array.Empty<string>());
            MissingRanker.Score(Item(1, "Es", 2017, new[] { "Horror", "Thriller" }, 8000, 7.2, 60), taste, Today).Reason.Should().Be("Passt zu deinen Genres: Horror, Thriller");

            var empty = new MissingRanker.TasteProfile(new Dictionary<string, double>(), new Dictionary<int, string>(), Array.Empty<string>());
            var ranked = MissingRanker.Rank(new[] { Item(1, "Schwach", 2000, Array.Empty<string>(), 20, 4.0, 1), Item(2, "Stark", 2010, Array.Empty<string>(), 9000, 8.0, 50) }, empty, Today);
            ranked.Select(i => i.TmdbId).Should().Equal(2, 1);
        }

        [Theory]
        [InlineData("clev", "Mac Millionär - Zu clever für ’nen Blanko-Scheck", false)]
        [InlineData("sword", "Merlin und Mim", false)]
        [InlineData("sword", "The Sword in the Stone", true)]
        [InlineData("conjuring", "Conjuring 4", true)]
        [InlineData("halloween", "Halloween II", true)]
        [InlineData("der weisse hai", "Der weiße Hai 2", true)]
        [InlineData("the", "The Thing", false)]
        public void Fits_OnlyWholeWordsOrTitleStart(string term, string title, bool expected)
        {
            MissingRanker.Fits(term, title).Should().Be(expected);
        }

        [Fact]
        public void FinalTerms_OnlyTheLongestTypingState()
        {
            MissingRanker.FinalTerms(new[] { "clevatess", "cleva", "clev", "cl", "Halloween", "the" })
                .Should().Equal("clevatess", "Halloween");
        }

        [Fact]
        public void Reason_OnlyMainGenres_AndFarFutureTitlesGoBack()
        {
            var profile = new MissingRanker.TasteProfile(new Dictionary<string, double> { ["Komödie"] = 1.0, ["Horror"] = 0.2 }, new Dictionary<int, string> { [111751] = "Blutgericht in Texas" }, Array.Empty<string>());
            var texas2 = Item(1, "The Texas Chainsaw Massacre Part 2", 1986, new[] { "Horror", "Thriller", "Komödie" }, 900, 5.9, 15);
            MissingRanker.Score(texas2, profile, Today).Reason.Should().BeNull("Komödie ist nicht das Hauptgenre");

            var fantasy = new MissingRanker.TasteProfile(new Dictionary<string, double> { ["Fantasy"] = 1.0, ["Action"] = 0.9 }, new Dictionary<int, string> { [9] = "Blade" }, Array.Empty<string>());
            var ghostRider = Item(2, "Ghost Rider", 2028, new[] { "Fantasy", "Action" }, 0, 0, 40);
            ghostRider.Released = false;
            ghostRider.ReleaseDate = "2028-03-01";
            var blade2 = Item(3, "Blade II", 2002, new[] { "Action", "Horror" }, 4000, 6.5, 25, collection: 9);
            MissingRanker.Rank(new[] { ghostRider, blade2 }, fantasy, Today).Select(i => i.TmdbId).Should().Equal(3, 2);
        }

        [Fact]
        public void Sections_LibraryTitlesBySectionMissingTitlesKeepTheirs()
        {
            const string json = "{\"welten\":[{\"name\":\"Disney\",\"abschnitte\":[{\"name\":\"Animation\"},{\"name\":\"Realverfilmungen\",\"min_stimmen_fehlend\":300},{\"name\":\"Sonstiges\"}],"
                + "\"studios\":[\"Walt Disney Pictures\"],"
                + "\"titel\":[{\"tmdb\":\"408\",\"typ\":\"film\",\"abschnitt\":\"Animation\"},{\"tmdb\":\"420818\",\"typ\":\"film\",\"abschnitt\":\"Realverfilmungen\"},{\"tmdb\":\"12230\",\"typ\":\"film\",\"abschnitt\":\"Animation\"}]}]}";
            var worlds = WorldDefinition.ParseJson(json);
            worlds[0].Sections.Select(s => s.Name).Should().Equal("Animation", "Realverfilmungen", "Sonstiges");
            worlds[0].Sections[1].MinMissingVotes.Should().Be(300);

            var snow = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), Tmdb = "408" };
            var lion = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), Tmdb = "420818" };
            var studioOnly = new WorldMembership.LibraryEntry { Id = Guid.NewGuid(), Tmdb = "999", Studios = new[] { "Walt Disney Pictures" } };
            var membership = WorldMembership.Compute(worlds, new[] { snow, lion, studioOnly }, Array.Empty<WorldMembership.CollectionEntry>());

            var sectionOf = membership.SectionOf[worlds[0].Id];
            sectionOf[snow.Id].Should().Be("Animation");
            sectionOf[lion.Id].Should().Be("Realverfilmungen");
            sectionOf[studioOnly.Id].Should().Be("Sonstiges", "Mitglieder ohne Listeneintrag landen im letzten Abschnitt");
            membership.Missing[worlds[0].Id].Should().Equal(new WorldMembership.MissingTitle(12230, false, "Animation"));
        }

        private static DiscoverItem Item(int id, string title, int year, string[] genres, int votes, double average, double popularity, int? collection = null) => new DiscoverItem
        {
            TmdbId = id,
            Title = title,
            Year = year,
            Genres = genres,
            VoteCount = votes,
            VoteAverage = average,
            Popularity = popularity,
            CollectionId = collection,
            Released = true
        };
    }
}
