using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Tageszeit, kurzfristiges Interesse und den Speicher der Suchen/angesehenen Titel.
    /// </summary>
    public class BehaviorTests
    {
        // 20:00 in Berlin (Sommerzeit, UTC+2) = 18:00 UTC
        private const int Cest = 120;

        [Theory]
        [InlineData(6, 0)]
        [InlineData(12, 1)]
        [InlineData(20, 2)]
        [InlineData(23, 3)]
        [InlineData(3, 3)]
        public void DaypartOf_LocalHour(int hour, int daypart)
        {
            DaypartProfile.DaypartOf(hour).Should().Be(daypart);
        }

        [Fact]
        public void DaypartAt_UsesTheUsersTimeZone()
        {
            var utc = new DateTime(2026, 10, 21, 16, 30, 0, DateTimeKind.Utc);

            DaypartProfile.DaypartAt(utc, 0).Should().Be(1, "16:30 UTC ist tagsüber");
            DaypartProfile.DaypartAt(utc, Cest).Should().Be(2, "in Berlin ist es 18:30, also abends");
        }

        [Fact]
        public void KindOrder_PutsWhatIsTypicalForTheDaypartFirst()
        {
            var profile = Profile();

            profile.KindOrder(2, Cest).Should().Equal(DaypartProfile.Movies, DaypartProfile.Series, DaypartProfile.LiveTv);
            profile.KindOrder(1, Cest).Should().Equal(DaypartProfile.Series, DaypartProfile.Movies, DaypartProfile.LiveTv);
        }

        [Fact]
        public void KindOrder_WithoutEnoughPlays_StaysDefault()
        {
            var profile = new DaypartProfile();
            profile.Add(At(20), DaypartProfile.Series, new[] { "Drama" });

            profile.KindOrder(2, Cest).Should().Equal(DaypartProfile.Movies, DaypartProfile.Series, DaypartProfile.LiveTv);
            profile.GenreScore(new[] { "Drama" }, 2, Cest).Should().Be(0);
        }

        [Fact]
        public void GenreScore_HighForGenresTypicalForTheDaypart()
        {
            var profile = Profile();

            profile.GenreScore(new[] { "Thriller" }, 2, Cest).Should().BeGreaterThan(0.2);
            profile.GenreScore(new[] { "Animation" }, 2, Cest).Should().Be(0);
            profile.GenreScore(new[] { "Animation" }, 1, Cest).Should().BeGreaterThan(0.2);
        }

        [Fact]
        public void Similarity_IsJaccardOfGenresAndTags()
        {
            InterestScoring.Similarity(new[] { "Krimi", "Drama" }, new[] { "krimi", "Thriller" }).Should().BeApproximately(1.0 / 3, 0.0001);
            InterestScoring.Similarity(Array.Empty<string>(), new[] { "Krimi" }).Should().Be(0);
        }

        [Fact]
        public void Decay_FallsOverDays()
        {
            InterestScoring.Decay(TimeSpan.Zero).Should().Be(1);
            InterestScoring.Decay(TimeSpan.FromDays(30)).Should().BeApproximately(Math.Exp(-1), 0.0001, "Standard: ein Drittel nach 30 Tagen");
            InterestScoring.Decay(TimeSpan.FromDays(7), 7).Should().BeApproximately(Math.Exp(-1), 0.0001);
        }

        [Fact]
        public void Combine_RepeatedInterestAddsUp_AtMostOne()
        {
            InterestScoring.Combine(new[] { 0.3, 0.2 }).Should().BeApproximately(0.5, 0.0001);
            InterestScoring.Combine(new[] { 0.6, 0.6, 0.6 }).Should().Be(1);
        }

        [Fact]
        public void Rerank_KeepsOrderWithoutBonus_MovesBoostedUp()
        {
            var items = new[] { "a", "b", "c", "d" };

            InterestScoring.Rerank(items, _ => 0).Should().Equal("a", "b", "c", "d");
            InterestScoring.Rerank(items, i => i == "d" ? 0.6 : 0).Should().Equal("a", "d", "b", "c");
        }

        [Fact]
        public void Store_NotesSearchesAndViews_DeduplicatesAndForgetsOnSwitchOff()
        {
            var store = new BehaviorStore(NullLogger<BehaviorStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-behavior-" + Guid.NewGuid().ToString("N") + ".json"));
            var user = Guid.NewGuid();
            var item = Guid.NewGuid();
            var now = DateTime.UtcNow;

            store.AddSearch(user, "Krimi", new[] { item }, now);
            store.AddSearch(user, "krimi", new[] { item }, now.AddMinutes(2));
            store.AddView(user, item, now);
            store.AddView(user, item, now.AddMinutes(30));
            store.ConfirmSearch(user, new[] { item }, now.AddMinutes(3));

            store.IsEnabled(user).Should().BeTrue("Standard an");
            store.Get(user).Searches.Should().ContainSingle();
            store.Get(user).Views.Should().ContainSingle();

            store.SetEnabled(user, false);
            store.IsEnabled(user).Should().BeFalse();
            store.Get(user).Searches.Should().BeEmpty();
            store.Get(user).Views.Should().BeEmpty();
        }

        [Fact]
        public void Store_KeepsAtMostMaxEntries_AndDropsOldEntries()
        {
            var store = new BehaviorStore(NullLogger<BehaviorStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-behavior-" + Guid.NewGuid().ToString("N") + ".json"), 30, 100);
            var user = Guid.NewGuid();
            var now = DateTime.UtcNow;
            store.AddView(user, Guid.NewGuid(), now.AddDays(-40));
            for (var i = 0; i < 120; i++)
            {
                store.AddView(user, Guid.NewGuid(), now.AddMinutes(i));
            }

            store.Get(user).Views.Should().HaveCount(100);
            store.Get(user).Views.Should().OnlyContain(v => v.At >= now);
        }

        [Fact]
        public void Store_DefaultsKeepAYearAnd1000Entries()
        {
            var store = new BehaviorStore(NullLogger<BehaviorStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-behavior-" + Guid.NewGuid().ToString("N") + ".json"));
            var user = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var version = store.Version;
            store.AddView(user, Guid.NewGuid(), now.AddDays(-200));
            store.AddView(user, Guid.NewGuid(), now.AddDays(-400));
            store.AddView(user, Guid.NewGuid(), now);

            store.RetentionDays.Should().Be(365);
            store.MaxEntries.Should().Be(1000);
            store.Version.Should().BeGreaterThan(version);
            store.Get(user).Views.Should().HaveCount(2, "200 Tage alt bleibt, 400 Tage alt nicht");
        }

        // Abends (Berlin) vor allem Filme mit Thriller, tagsüber vor allem Serien mit Animation
        private static DaypartProfile Profile()
        {
            var profile = new DaypartProfile();
            for (var i = 0; i < 20; i++)
            {
                profile.Add(At(20), DaypartProfile.Movies, new[] { "Thriller" });
                profile.Add(At(13), DaypartProfile.Series, new[] { "Animation" });
            }

            for (var i = 0; i < 4; i++)
            {
                profile.Add(At(20), DaypartProfile.Series, new[] { "Drama" });
                profile.Add(At(13), DaypartProfile.Movies, new[] { "Drama" });
            }

            return profile;
        }

        // Ortszeit Berlin (UTC+2) → UTC
        private static DateTime At(int localHour) => new DateTime(2026, 10, 1, localHour, 0, 0, DateTimeKind.Utc).AddMinutes(-Cest);
    }
}
