using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Mindestbewertung und Gewicht der Bewertung.
    /// </summary>
    public class RatingQualityTests
    {
        [Theory]
        [InlineData(2.2f, true)]
        [InlineData(4.5f, true)]
        [InlineData(5.0f, false)]
        [InlineData(9.5f, false)]
        [InlineData(null, false)]
        [InlineData(0f, false)]
        public void WeaklyRatedTitles_AreFiltered_UnratedAreKept(float? rating, bool expected)
        {
            RatingQuality.IsBelowMinimum(Item(rating), new PluginConfiguration()).Should().Be(expected);
        }

        [Fact]
        public void MinimumZero_FiltersNothing()
        {
            RatingQuality.IsBelowMinimum(Item(1.0f), new PluginConfiguration { MinCommunityRating = 0 }).Should().BeFalse();
        }

        [Fact]
        public void BetterRatedTitle_WinsWithSameSimilarity()
        {
            var config = new PluginConfiguration();
            RatingQuality.Apply(0.6, Item(9.5f), config).Should().BeGreaterThan(RatingQuality.Apply(0.6, Item(5.5f), config));
            RatingQuality.Apply(0.6, Item(9.5f), new PluginConfiguration { RatingQualityWeight = 0 }).Should().Be(0.6);
        }

        private static MediaItemMetadata Item(float? rating)
        {
            return new MediaItemMetadata(Guid.NewGuid(), "Test", MediaType.Series) { CommunityRating = rating };
        }
    }
}
