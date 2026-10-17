using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.Api
{
    /// <summary>
    /// Tests für die Rangfolge von „Top 10 bei euch“ (GET /LocalRecs/Top10).
    /// </summary>
    public class Top10ControllerTests
    {
        private static readonly Guid A = Guid.NewGuid();
        private static readonly Guid B = Guid.NewGuid();
        private static readonly Guid C = Guid.NewGuid();
        private static readonly Guid User1 = Guid.NewGuid();
        private static readonly Guid User2 = Guid.NewGuid();

        [Fact]
        public void Rank_MoreViewersBeatMorePlays()
        {
            var usages = new[] { (A, User1, 9), (B, User1, 1), (B, User2, 1), (C, User2, 2) };

            Top10Controller.Rank(usages, 10).Should().ContainInOrder(B, A, C);
        }

        [Fact]
        public void Rank_EpisodesOfOneSeriesAddUp()
        {
            var usages = new[] { (A, User1, 1), (A, User1, 1), (B, User1, 1) };

            Top10Controller.Rank(usages, 1).Should().Equal(A);
        }
    }
}
