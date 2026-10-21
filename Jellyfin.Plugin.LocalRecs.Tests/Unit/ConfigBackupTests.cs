using FluentAssertions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests: Sicherungen der Einstellungen enthalten keine Schlüssel.
    /// </summary>
    public class ConfigBackupTests
    {
        [Fact]
        public void WithoutSecrets_EmptiesKeys_KeepsEverythingElse()
        {
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><PluginConfiguration xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<TmdbApiKey>tmdb-geheim</TmdbApiKey><TraktClientId>oeffentlich</TraktClientId><TraktClientSecret>trakt-geheim</TraktClientSecret>"
                + "<FanartApiKey>fanart-geheim</FanartApiKey><MovieRecommendationCount>25</MovieRecommendationCount></PluginConfiguration>";

            var clean = Plugin.WithoutSecrets(xml);

            clean.Should().NotContain("geheim");
            clean.Should().Contain("<TraktClientId>oeffentlich</TraktClientId>").And.Contain("<MovieRecommendationCount>25</MovieRecommendationCount>");
            clean.Should().StartWith("<?xml");
            Plugin.WithoutSecrets("kaputt").Should().BeNull();
        }
    }
}
