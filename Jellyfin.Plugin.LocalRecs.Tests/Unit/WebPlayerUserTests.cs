using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Freigabe des Netflix-Players pro Benutzer.
    /// </summary>
    public class WebPlayerUserTests
    {
        [Fact]
        public void IsPlayerUser_ComparesIdsWithoutDashesAndCase()
        {
            var ids = new[] { "4A5B6814783B43E78BB97AE78AB31FEA" };

            WebController.IsPlayerUser(ids, "4a5b6814-783b-43e7-8bb9-7ae78ab31fea").Should().BeTrue();
            WebController.IsPlayerUser(ids, "70c6366ba3fe48f9bfb74911d1e70416").Should().BeFalse();
            WebController.IsPlayerUser(null, "4a5b6814783b43e78bb97ae78ab31fea").Should().BeFalse();
            WebController.IsPlayerUser(ids, null).Should().BeFalse();
        }
    }
}
