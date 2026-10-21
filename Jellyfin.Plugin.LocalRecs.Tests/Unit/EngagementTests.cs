using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests: wie stark ein Titel nach Abschluss, Wiederholung und Serienfortschritt zählt.
    /// </summary>
    public class EngagementTests
    {
        private const long Hour = 36_000_000_000;

        [Theory]
        [InlineData(0, 1f)]
        [InlineData(1, 1f)]
        [InlineData(2, 1.25f)]
        [InlineData(4, 1.75f)]
        [InlineData(10, 1.75f)]
        public void RewatchFactor_GrowsWithPlayCount_Capped(int plays, float expected)
        {
            UserProfileService.RewatchFactor(plays).Should().BeApproximately(expected, 0.0001f);
        }

        [Fact]
        public void ProgressFactor_OnlyBetweenAQuarterAndPlayed()
        {
            UserProfileService.ProgressFactor(Hour / 10, Hour).Should().Be(0f, "unter einem Viertel zählt als abgebrochen bzw. noch nichts");
            UserProfileService.ProgressFactor(Hour / 4, Hour).Should().BeApproximately(0.425f, 0.0001f);
            UserProfileService.ProgressFactor(Hour * 8 / 10, Hour).Should().BeApproximately(0.7f, 0.0001f);
            UserProfileService.ProgressFactor(Hour * 95 / 100, Hour).Should().Be(0f, "ab 90 % markiert Jellyfin den Film als gesehen");
            UserProfileService.ProgressFactor(Hour, null).Should().Be(0f);
        }

        [Fact]
        public void SeriesFactor_ByShareOfWatchedEpisodes()
        {
            UserProfileService.SeriesFactor(1, 100).Should().BeApproximately(0.608f, 0.0001f);
            UserProfileService.SeriesFactor(50, 100).Should().BeApproximately(1.0f, 0.0001f);
            UserProfileService.SeriesFactor(100, 100).Should().BeApproximately(1.4f, 0.0001f);
            UserProfileService.SeriesFactor(0, 0).Should().Be(1f);
        }
    }
}
