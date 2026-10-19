using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Web;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für das Einhängen des Web-Moduls in die index.html von Jellyfin Web.
    /// </summary>
    public class WebIntegrationTests
    {
        private const string Html = "<html><head><title>Jellyfin</title></head><body></body></html>";

        [Fact]
        public void InjectLoader_AddsScriptAndStyleWithVersion_BeforeHeadEnd()
        {
            var result = WebIntegration.InjectLoader(Html, "12.18.0.0");

            result.Should().Contain("localrecs-web.js?v='+v").And.Contain("localrecs-web.css?v='+v").And.Contain("v='12.18.0.0'");
            result.IndexOf(WebIntegration.Marker, System.StringComparison.Ordinal).Should().BeLessThan(result.IndexOf("</head>", System.StringComparison.Ordinal));
        }

        [Fact]
        public void InjectLoader_IsIdempotent_AndLeavesFilesWithoutHeadAlone()
        {
            var once = WebIntegration.InjectLoader(Html, "1");

            WebIntegration.InjectLoader(once, "1").Should().Be(once, "ein zweites Einhängen würde das Skript doppelt laden");
            WebIntegration.InjectLoader("<div>kein Kopf</div>", "1").Should().Be("<div>kein Kopf</div>");
        }

        [Fact]
        public void TransformIndex_UsesPayloadContents()
        {
            WebIntegration.TransformIndex(new WebIntegration.TransformationPayload { Contents = Html })
                .Should().Contain(WebIntegration.Marker);
        }
    }
}
