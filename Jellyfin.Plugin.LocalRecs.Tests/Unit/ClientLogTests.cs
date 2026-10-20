using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Api;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Diagnose-Zeilen der Weboberfläche (keine Zugangsschlüssel im Serverlog).
    /// </summary>
    public class ClientLogTests
    {
        [Theory]
        [InlineData("GET /Videos/1/main.m3u8?ApiKey=abc123def&MediaSourceId=9", "ApiKey=***&MediaSourceId=9")]
        [InlineData("url?api_key=geheim", "api_key=***")]
        [InlineData("MediaBrowser Token=\"0123456789abcdef\", Client=x", "Token=\"***\"")]
        [InlineData("X-Emby-Token: 0123abcd", "X-Emby-Token: ***")]
        public void Sanitize_RemovesAccessKeys(string line, string expected)
        {
            var result = ClientLogController.Sanitize(line);

            result.Should().Contain(expected);
            result.Should().NotContain("abc123def").And.NotContain("geheim").And.NotContain("0123456789abcdef").And.NotContain("0123abcd");
        }

        [Fact]
        public void Sanitize_RemovesControlCharacters_AndShortens()
        {
            var result = ClientLogController.Sanitize("Zeile\r\nGefälscht: Eintrag" + new string('x', 5000));

            result.Should().NotContain("\n").And.NotContain("\r");
            result.Length.Should().BeLessOrEqualTo(ClientLogController.MaxLineLength + 1);
        }

        [Fact]
        public void Sanitize_KeepsNormalText()
        {
            ClientLogController.Sanitize("Video playing t=12 rs=4").Should().Be("Video playing t=12 rs=4");
            ClientLogController.Sanitize(null).Should().BeEmpty();
        }
    }
}
