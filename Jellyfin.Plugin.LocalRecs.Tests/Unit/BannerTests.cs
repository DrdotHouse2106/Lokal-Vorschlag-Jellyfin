using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Banner;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für den Banner des Tages.
    /// </summary>
    public class BannerTests
    {
        private static readonly Guid User = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private static readonly int[] Ranked = Enumerable.Range(0, 30).ToArray();

        [Fact]
        public void SameUserAndDay_SamePick()
        {
            var day = new DateOnly(2026, 10, 21);

            var first = BannerPicker.Pick(Ranked, _ => 1, 5, BannerPicker.Seed(User, day));
            var again = BannerPicker.Pick(Ranked, _ => 1, 5, BannerPicker.Seed(User, day));

            first.Should().HaveCount(5).And.OnlyHaveUniqueItems();
            again.Should().Equal(first);
        }

        [Fact]
        public void ChangesFromDayToDay()
        {
            var picks = Enumerable.Range(0, 7)
                .Select(d => BannerPicker.Pick(Ranked, _ => 1, 5, BannerPicker.Seed(User, new DateOnly(2026, 10, 1).AddDays(d))))
                .ToList();

            picks.Select(p => string.Join(",", p.OrderBy(x => x))).Distinct().Should().HaveCount(7, "jeder Tag eine andere Auswahl");
            picks.SelectMany(p => p).Distinct().Count().Should().BeGreaterThan(10, "über eine Woche kommen viele verschiedene Titel dran");
        }

        [Fact]
        public void BetterRankedTitles_ComeMoreOften()
        {
            var counts = new int[30];
            for (var d = 0; d < 400; d++)
            {
                foreach (var i in BannerPicker.Pick(Ranked, _ => 1, 5, BannerPicker.Seed(User, new DateOnly(2025, 1, 1).AddDays(d))))
                {
                    counts[i]++;
                }
            }

            counts.Take(5).Sum().Should().BeGreaterThan(counts.Skip(25).Sum());
        }

        [Fact]
        public void YesterdaysTitles_ComeBackRarely()
        {
            var withPenalty = 0;
            var without = 0;
            for (var d = 0; d < 300; d++)
            {
                var seed = BannerPicker.Seed(User, new DateOnly(2025, 1, 1).AddDays(d));
                without += BannerPicker.Pick(Ranked, _ => 1, 5, seed).Count(i => i < 5);
                withPenalty += BannerPicker.Pick(Ranked, i => i < 5 ? BannerService.RecentFactor(1) : 1, 5, seed).Count(i => i < 5);
            }

            withPenalty.Should().BeLessThan(without / 2);
        }

        [Fact]
        public void Factors()
        {
            BannerService.RecentFactor(null).Should().Be(1);
            BannerService.RecentFactor(1).Should().BeLessThan(BannerService.RecentFactor(2));
            BannerService.RecentFactor(8).Should().Be(1);
            BannerService.IgnoredFactor(3).Should().Be(1);
            BannerService.IgnoredFactor(20).Should().Be(0.5);
            BannerService.IgnoredFactor(40).Should().Be(0.3);
        }

        [Fact]
        public void Interleave_AlternatesMoviesAndSeries()
        {
            BannerPicker.Interleave(new[] { 1, 3, 5 }, new[] { 2 }).Should().Equal(1, 2, 3, 5);
        }

        [Fact]
        public void Store_RemembersDays_AndRecentlyShown()
        {
            var store = new BannerStore(NullLogger<BannerStore>.Instance, Path.Combine(Path.GetTempPath(), "lr-banner-" + Guid.NewGuid().ToString("N") + ".json"));
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var today = new DateOnly(2026, 10, 21);
            store.Set(User, today.AddDays(-3), new[] { a });
            store.Set(User, today.AddDays(-1), new[] { a, b });
            store.Set(User, today.AddDays(-20), new[] { Guid.NewGuid() });

            store.Get(User, today.AddDays(-1)).Should().Equal(a, b);
            store.Get(User, today).Should().BeNull();
            var recent = store.RecentlyShown(User, today);
            recent.Should().HaveCount(2);
            recent[a].Should().Be(1);
            recent[b].Should().Be(1);
        }
    }
}
