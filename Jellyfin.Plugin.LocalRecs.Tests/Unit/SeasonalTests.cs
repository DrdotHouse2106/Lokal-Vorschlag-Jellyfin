using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Seasonal;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für „Passend zur Jahreszeit“.
    /// </summary>
    public class SeasonalTests
    {
        [Theory]
        [InlineData(2025, 4, 20)]
        [InlineData(2026, 4, 5)]
        [InlineData(2027, 3, 28)]
        [InlineData(2038, 4, 25)]
        public void EasterSunday_Gauss(int year, int month, int day)
        {
            SeasonCalendar.EasterSunday(year).Should().Be(new DateOnly(year, month, day));
        }

        [Theory]
        [InlineData(2025, 11, 30)]
        [InlineData(2026, 11, 29)]
        [InlineData(2027, 11, 28)]
        [InlineData(2022, 11, 27)]
        public void FirstAdvent(int year, int month, int day)
        {
            SeasonCalendar.FirstAdvent(year).Should().Be(new DateOnly(year, month, day));
        }

        [Theory]
        [InlineData("2026-11-28", null)]
        [InlineData("2026-11-29", "weihnachten")]
        [InlineData("2026-12-26", "weihnachten")]
        [InlineData("2026-12-27", "silvester")]
        [InlineData("2027-01-01", "silvester")]
        [InlineData("2027-01-02", null)]
        [InlineData("2026-10-14", null)]
        [InlineData("2026-10-15", "halloween")]
        [InlineData("2026-10-31", "halloween")]
        [InlineData("2026-11-01", null)]
        [InlineData("2026-04-02", null)]
        [InlineData("2026-04-03", "ostern")]
        [InlineData("2026-04-06", "ostern")]
        [InlineData("2026-04-07", null)]
        [InlineData("2026-02-10", null)]
        public void SeasonOf_Windows(string date, string? expected)
        {
            SeasonCalendar.SeasonOf(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), false)?.Id.Should().Be(expected);
            if (expected == null)
            {
                SeasonCalendar.SeasonOf(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), false).Should().BeNull();
            }
        }

        [Fact]
        public void Valentine_OnlyWhenSwitchedOn()
        {
            SeasonCalendar.SeasonOf(new DateOnly(2026, 2, 10), true)!.Id.Should().Be(SeasonCalendar.Valentine);
            SeasonCalendar.SeasonOf(new DateOnly(2026, 2, 10), false).Should().BeNull();
        }

        [Theory]
        [InlineData("Weihnachtsfilm", "Der kleine Lord – Weihnachtsspecial", true)]
        [InlineData("Weihnachtsfilm", "The Christmas Chronicles", true)]
        [InlineData("Weihnachtsfilm", "Adventure Time", false)]
        [InlineData("Weihnachtsfilm", "Advent", false)]
        [InlineData("Weihnachtsfilm", "Christmas-Special: Vom Nordpol", true)]
        [InlineData("Halloween", "Halloween II", true)]
        [InlineData("Halloween", "Treehouse of Horror", false)]
        [InlineData("Silvester", "Dinner for One – Silvester", true)]
        [InlineData("Ostern", "Postern", false)]
        public void Title_WholeWordsOnly(string _, string title, bool expected)
        {
            var season = title.Contains("Halloween", StringComparison.Ordinal) || title.Contains("Horror", StringComparison.Ordinal) ? SeasonCalendar.Get(SeasonCalendar.Halloween)
                : title.Contains("Silvester", StringComparison.Ordinal) ? SeasonCalendar.Get(SeasonCalendar.NewYear)
                : title.Contains("Postern", StringComparison.Ordinal) ? SeasonCalendar.Get(SeasonCalendar.Easter)
                : SeasonCalendar.Get(SeasonCalendar.Christmas);

            season!.MatchesTitle(title).Should().Be(expected);
        }

        [Fact]
        public void Keywords_AndTags_WholeKeywordOnly()
        {
            var christmas = SeasonCalendar.Get(SeasonCalendar.Christmas)!;

            SeasonalService.Matches(christmas, "Stirb langsam", null, new[] { "christmas", "terrorism" }).Should().BeTrue();
            SeasonalService.Matches(christmas, "Irgendwas", new[] { "Weihnachten" }, null).Should().BeTrue();
            SeasonalService.Matches(christmas, "Irgendwas", new[] { "christmas island" }, new[] { "adventure" }).Should().BeFalse();
        }

        [Fact]
        public void ParseKeywords_MoviesAndSeries()
        {
            SeasonalKeywordLookup.ParseKeywords("{\"id\":1,\"keywords\":[{\"id\":207317,\"name\":\"Christmas\"},{\"id\":3,\"name\":\"snow\"}]}").Should().Equal("christmas", "snow");
            SeasonalKeywordLookup.ParseKeywords("{\"id\":2,\"results\":[{\"id\":3335,\"name\":\"halloween\"}]}").Should().Equal("halloween");
            SeasonalKeywordLookup.Key(true, "1399").Should().Be("tv:1399");
        }
    }
}
