using System;
using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.Api
{
    /// <summary>
    /// Tests für den Endpunkt der TV-Apps (GET /LocalRecs/Recommendations).
    /// </summary>
    public class RecommendationsControllerTests
    {
        [Fact]
        public void GetUserId_ReadsJellyfinClaim()
        {
            var id = Guid.NewGuid();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", id.ToString()) }, "Test"));

            RecommendationsController.GetUserId(principal).Should().Be(id);
        }

        [Fact]
        public void GetUserId_WithoutClaim_ReturnsEmpty()
        {
            RecommendationsController.GetUserId(new ClaimsPrincipal(new ClaimsIdentity())).Should().Be(Guid.Empty);
            RecommendationsController.GetUserId(null).Should().Be(Guid.Empty);
        }

        [Theory]
        [InlineData(null, 5)]
        [InlineData(0, 5)]
        [InlineData(3, 3)]
        [InlineData(10, 5)]
        public void Limit_ShortensOnlyWhenNeeded(int? limit, int expected)
        {
            var items = Enumerable.Range(0, 5).Select(i => new BaseItemDto { Name = "Titel " + i }).ToList();
            var result = new QueryResult<BaseItemDto>(null, items.Count, items);

            var limited = RecommendationsController.Limit(result, limit);

            limited.Items.Should().HaveCount(expected);
            limited.Items[0].Name.Should().Be("Titel 0");
        }
    }
}
