using System;
using System.IO;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Suchbegriffe: Füllwörter, Mindestlänge, passender Titel, nur abgeschlossene Suchen.
    /// </summary>
    public class SearchTermsTests
    {
        [Theory]
        [InlineData("The", false)]
        [InlineData("the ", false)]
        [InlineData("Der", false)]
        [InlineData("de", false)]
        [InlineData("x", false)]
        [InlineData("The a of", false)]
        [InlineData("Dea", true)]
        [InlineData("Deadpool", true)]
        [InlineData("The Boys", true)]
        [InlineData("Her", true)]
        [InlineData("300", true)]
        [InlineData("Tom Hanks", true)]
        public void IsMeaningful(string term, bool expected)
        {
            SearchTerms.IsMeaningful(term).Should().Be(expected);
        }

        [Theory]
        [InlineData("dead", "Deadpool", true)]
        [InlineData("Deadpool", "Deadpool & Wolverine", true)]
        [InlineData("the boys", "The Boys", true)]
        [InlineData("The", "Deadpool", false)]
        [InlineData("The", "The Boys", false)]
        [InlineData("pool", "Deadpool", true)]
        [InlineData("boys", "Deadpool", false)]
        [InlineData("Amelie", "Die fabelhafte Welt der Amélie", true)]
        [InlineData("weisse hai", "Der weiße Hai", true)]
        public void TitleFits(string term, string title, bool expected)
        {
            SearchTerms.TitleFits(term, title).Should().Be(expected);
        }

        [Fact]
        public void Store_OnlyConfirmedSearchesStay_TypingStepsReplaceEachOther()
        {
            var store = new BehaviorStore(NullLogger<BehaviorStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-behavior-" + Guid.NewGuid().ToString("N") + ".json"));
            var user = Guid.NewGuid();
            var deadpool = Guid.NewGuid();
            var other = Guid.NewGuid();
            var now = DateTime.UtcNow;

            store.AddSearch(user, "Dead", new[] { deadpool, other }, now);
            store.AddSearch(user, "Deadpo", new[] { deadpool }, now.AddSeconds(2));
            store.AddSearch(user, "Deadpool", new[] { deadpool }, now.AddSeconds(4));
            store.Get(user).Searches.Should().ContainSingle().Which.Term.Should().Be("Deadpool");
            store.Get(user).Searches[0].Confirmed.Should().BeFalse();

            store.ConfirmSearch(user, new[] { other }, now.AddMinutes(1)).Should().BeFalse("kein Treffer dieser Suche");
            store.ConfirmSearch(user, new[] { deadpool }, now.AddMinutes(1)).Should().BeTrue();
            store.Get(user).Searches[0].Confirmed.Should().BeTrue();

            // Bestätigt bleibt sie, auch wenn danach weiter getippt wird
            store.AddSearch(user, "Deadpool 2", new[] { deadpool }, now.AddMinutes(2));
            store.Get(user).Searches.Should().HaveCount(2);
        }

        [Fact]
        public void Store_UnconfirmedSearchesAreDroppedAfterAnHour_AndConfirmOnlyWithin30Minutes()
        {
            var store = new BehaviorStore(NullLogger<BehaviorStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-behavior-" + Guid.NewGuid().ToString("N") + ".json"));
            var user = Guid.NewGuid();
            var item = Guid.NewGuid();
            var now = DateTime.UtcNow;
            store.AddSearch(user, "Krimi", new[] { item }, now.AddHours(-2));
            store.AddSearch(user, "Western", new[] { item }, now.AddMinutes(-40));
            store.ConfirmSearch(user, new[] { item }, now).Should().BeFalse("zu lange her");
            store.AddView(user, Guid.NewGuid(), now);
            store.Get(user).Searches.Should().ContainSingle().Which.Term.Should().Be("Western");
        }
    }
}
