using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Collections;
using Jellyfin.Plugin.LocalRecs.Moments;
using Jellyfin.Plugin.LocalRecs.Seasonal;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die zeitabhängigen Empfehlungen (12.46): Sehrhythmus, Jubiläen, TV-Abgleich, Kinostart und Jahreszeit-Reihenfolge.
    /// </summary>
    public class MomentsTests
    {
        [Theory]
        [InlineData(2026, 10, 23, 20, true)] // Freitag 20 Uhr
        [InlineData(2026, 10, 25, 1, true)] // Sonntag 1 Uhr = Samstagabend
        [InlineData(2026, 10, 25, 20, false)] // Sonntag 20 Uhr
        [InlineData(2026, 10, 21, 21, false)] // Mittwoch
        [InlineData(2026, 10, 24, 12, false)] // Samstag mittags
        public void WeekendEvening(int y, int m, int d, int h, bool expected)
        {
            WatchRhythm.IsWeekendEvening(new DateTime(y, m, d, h, 0, 0)).Should().Be(expected);
        }

        [Fact]
        public void RuntimeFactor_FitsBeforeBedtime()
        {
            WatchRhythm.RuntimeFactor(90, 120, false).Should().Be(1);
            WatchRhythm.RuntimeFactor(130, 100, false).Should().Be(0.6);
            WatchRhythm.RuntimeFactor(180, 60, false).Should().Be(0.35);
            WatchRhythm.RuntimeFactor(140, null, true).Should().Be(0.6);
            WatchRhythm.RuntimeFactor(25, 10, true).Should().Be(1);
            WatchRhythm.RuntimeFactor(null, 0, true).Should().Be(1);
        }

        [Fact]
        public void Rhythm_DefaultsWithoutHistory()
        {
            var rhythm = new WatchRhythm();
            rhythm.Bedtime(0).Should().Be(WatchRhythm.DefaultBedtime);
            rhythm.PreferShort(new DateTime(2026, 10, 21, 20, 0, 0), 0).Should().BeTrue(); // Mittwochabend
            rhythm.PreferShort(new DateTime(2026, 10, 23, 20, 0, 0), 0).Should().BeFalse(); // Freitagabend
            rhythm.PreferShort(new DateTime(2026, 10, 21, 10, 0, 0), 0).Should().BeFalse(); // vormittags
            rhythm.RemainingMinutes(new DateTime(2026, 10, 21, 22, 0, 0), 0).Should().Be(90);
            rhythm.RemainingMinutes(new DateTime(2026, 10, 21, 15, 0, 0), 0).Should().BeNull();
        }

        [Fact]
        public void Rhythm_LearnsBedtimeAfterMidnight()
        {
            var rhythm = new WatchRhythm();
            for (var day = 1; day <= 10; day++)
            {
                rhythm.Add(new DateTime(2026, 9, day, 21, 0, 0, DateTimeKind.Utc), 45);
                rhythm.Add(new DateTime(2026, 9, day + 1, 0, 30, 0, DateTimeKind.Utc), 45);
            }

            rhythm.Bedtime(0).Should().Be(24.5);
            rhythm.RemainingMinutes(new DateTime(2026, 10, 21, 23, 0, 0), 0).Should().Be(90);
        }

        [Fact]
        public void Rhythm_LearnsLongMoviesOnWeekdays()
        {
            var rhythm = new WatchRhythm();
            for (var i = 0; i < 12; i++)
            {
                // Mittwochabende: lange Filme
                rhythm.Add(new DateTime(2026, 7, 1, 20, 0, 0, DateTimeKind.Utc).AddDays(7 * i), 140);
            }

            rhythm.PreferShort(new DateTime(2026, 10, 21, 20, 0, 0), 0).Should().BeFalse();
        }

        [Theory]
        [InlineData(2001, 10, 22, 25)]
        [InlineData(1996, 10, 22, 30)]
        [InlineData(2016, 10, 22, 10)]
        [InlineData(1951, 10, 22, 75)]
        public void RoundAnniversary_Matches(int year, int month, int day, int expected)
        {
            MomentsService.RoundAnniversary(new DateTime(year, month, day), new DateOnly(2026, 10, 22)).Should().Be(expected);
        }

        [Theory]
        [InlineData(2011, 10, 22)] // 15 Jahre: nicht rund
        [InlineData(2021, 10, 22)] // 5 Jahre: zu kurz
        [InlineData(2001, 10, 21)] // falscher Tag
        public void RoundAnniversary_Skips(int year, int month, int day)
        {
            MomentsService.RoundAnniversary(new DateTime(year, month, day), new DateOnly(2026, 10, 22)).Should().BeNull();
        }

        [Fact]
        public void RoundAnniversary_LeapDayOn28February()
        {
            MomentsService.RoundAnniversary(new DateTime(1996, 2, 29), new DateOnly(2026, 2, 28)).Should().Be(30);
            MomentsService.RoundAnniversary(new DateTime(2004, 2, 29), new DateOnly(2024, 2, 29)).Should().Be(20);
        }

        [Theory]
        [InlineData("Die Hard: Stirb langsam", "diehardstirblangsam")]
        [InlineData("Amélie", "amelie")]
        [InlineData("Der weiße Hai", "derweissehai")]
        [InlineData("  ", "")]
        public void NormalizeTitle(string title, string expected)
        {
            MomentsService.NormalizeTitle(title).Should().Be(expected);
        }

        [Fact]
        public void BroadcastTime_TodayAndNight()
        {
            var today = new DateOnly(2026, 10, 22);
            MomentsService.BroadcastTime(new DateTime(2026, 10, 22, 20, 15, 0), today).Should().Be("Heute 20:15");
            MomentsService.BroadcastTime(new DateTime(2026, 10, 23, 0, 30, 0), today).Should().Be("Heute Nacht 00:30");
        }

        [Fact]
        public void ParseCollectionParts_AndRegionRelease()
        {
            var parts = MovieSeriesLookup.ParseCollectionParts("{\"parts\":[{\"id\":1,\"title\":\"Teil 1\",\"release_date\":\"2020-01-01\"},{\"id\":2,\"title\":\"Teil 2\",\"release_date\":null}]}");
            parts.Should().HaveCount(2);
            parts[1].ReleaseDate.Should().BeEmpty();

            const string Dates = "{\"results\":[{\"iso_3166_1\":\"US\",\"release_dates\":[{\"type\":3,\"release_date\":\"2026-11-01T00:00:00.000Z\"}]},"
                + "{\"iso_3166_1\":\"DE\",\"release_dates\":[{\"type\":1,\"release_date\":\"2026-10-01T00:00:00.000Z\"},{\"type\":3,\"release_date\":\"2026-11-13T00:00:00.000Z\"}]}]}";
            MovieSeriesLookup.ParseRegionRelease(Dates, "DE").Should().Be("2026-11-13");
            MovieSeriesLookup.ParseRegionRelease(Dates, "FR").Should().BeNull();
        }

        [Theory]
        [InlineData("/tv/Solar Opposites/Season 00/Solar.Opposites.S00E03.Comic-Con.Home.Panel.mkv", false)]
        [InlineData("/tv/Solar Opposites/Season 00/Solar Opposites S00E03 Halloween Special.mkv", true)]
        [InlineData("/tv/Solar Opposites/Season 00/Solar Opposites - S00E03.mkv", true)]
        [InlineData(null, true)]
        public void Specials_FileNameMustAgree(string? path, bool expected)
        {
            SeasonalService.FileNameAgrees(SeasonCalendar.Get(SeasonCalendar.Halloween)!, path, "Solar Opposites").Should().Be(expected);
        }

        [Fact]
        public void SeasonalOrder_MoviesFirstAndOneEpisodePerSeriesInFront()
        {
            var seriesA = Guid.NewGuid();
            var seriesB = Guid.NewGuid();
            var items = new List<BaseItem>();
            for (var i = 0; i < 3; i++)
            {
                items.Add(new Episode { Id = Guid.NewGuid(), SeriesId = seriesA, Name = "A" + i, SortName = "A" + i, CommunityRating = 9 });
            }

            items.Add(new Episode { Id = Guid.NewGuid(), SeriesId = seriesB, Name = "B0", SortName = "B0", CommunityRating = 5 });
            items.Add(new Movie { Id = Guid.NewGuid(), Name = "Film", SortName = "Film", CommunityRating = 6 });

            var ordered = SeasonalService.Order(items, _ => false, new Dictionary<Guid, int>());
            ordered[0].Name.Should().Be("Film");
            ordered.Skip(1).Take(2).Select(i => ((Episode)i).SeriesId).Should().OnlyHaveUniqueItems();
            ordered.Should().HaveCount(5);
        }
    }
}
