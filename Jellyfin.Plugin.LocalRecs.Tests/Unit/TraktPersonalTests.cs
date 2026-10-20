using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die persönlichen Trakt-Empfehlungen.
    /// </summary>
    public class TraktPersonalTests
    {
        private static readonly Guid Ulberding = Guid.Parse("4f1c2d3e-0000-4000-8000-000000000001");

        private static string PluginXml(string token) => "<?xml version=\"1.0\"?><PluginConfiguration xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><TraktUsers>"
            + "<TraktUser><AccessToken>anderer</AccessToken><RefreshToken>r0</RefreshToken><LinkedMbUserId>11111111-2222-3333-4444-555555555555</LinkedMbUserId></TraktUser>"
            + "<TraktUser><AccessToken>" + token + "</AccessToken><RefreshToken>r1</RefreshToken><LinkedMbUserId>" + Ulberding.ToString("D") + "</LinkedMbUserId></TraktUser>"
            + "</TraktUsers></PluginConfiguration>";

        [Fact]
        public void ReadPluginToken_FindsTokenOfLinkedUser()
        {
            TraktPersonalService.ReadPluginToken(PluginXml("geheim"), Ulberding).Should().Be("geheim");
            TraktPersonalService.ReadPluginToken(PluginXml("geheim"), Guid.NewGuid()).Should().BeNull();
            TraktPersonalService.ReadPluginToken("kein xml", Ulberding).Should().BeNull();
        }

        [Fact]
        public async Task Recommendations_UsePluginLinkReadOnly_WithItsClientId_AndAreCached()
        {
            var (service, requests, pluginFile) = Service(_ => Json("[{\"title\":\"x\",\"ids\":{\"tmdb\":604}},{\"ids\":{\"tmdb\":27205}}]"));
            File.WriteAllText(pluginFile, PluginXml("geheim"));
            var before = File.ReadAllText(pluginFile);
            var config = new PluginConfiguration();

            var first = await service.GetRecommendationsAsync(Ulberding, config, CancellationToken.None);
            var second = await service.GetRecommendationsAsync(Ulberding, config, CancellationToken.None);

            first!.Movies.Should().Equal("604", "27205");
            first.Source.Should().Be(TraktPersonalService.SourcePlugin);
            second.Should().BeSameAs(first);
            requests.Should().HaveCount(2, "Filme und Serien einmal, dann aus dem Zwischenspeicher");
            requests[0].RequestUri!.ToString().Should().StartWith("https://api.trakt.tv/recommendations/movies");
            requests[1].RequestUri!.ToString().Should().StartWith("https://api.trakt.tv/recommendations/shows");
            requests[0].Headers.Authorization!.Parameter.Should().Be("geheim");
            requests[0].Headers.GetValues("trakt-api-key").Should().Equal("plugin-client");
            requests.Should().OnlyContain(r => r.Method == HttpMethod.Get && r.Content == null, "es wird nichts an Trakt gesendet");
            File.ReadAllText(pluginFile).Should().Be(before, "die Datei des Trakt-Plugins wird nur gelesen");
        }

        [Fact]
        public async Task Recommendations_OffForUser_OrWithoutLink_OrFeatureOff()
        {
            var (service, requests, pluginFile) = Service(_ => Json("[]"));
            File.WriteAllText(pluginFile, PluginXml("geheim"));

            (await service.GetRecommendationsAsync(Guid.NewGuid(), new PluginConfiguration(), CancellationToken.None)).Should().BeNull();
            (await service.GetRecommendationsAsync(Ulberding, new PluginConfiguration { EnableTraktPersonal = false }, CancellationToken.None)).Should().BeNull();
            service.SetEnabled(Ulberding, false);
            (await service.GetRecommendationsAsync(Ulberding, new PluginConfiguration(), CancellationToken.None)).Should().BeNull();
            service.GetStatus(Ulberding, new PluginConfiguration()).Should().BeEquivalentTo(new
            {
                Enabled = false,
                Source = TraktPersonalService.SourcePlugin,
                PluginLinked = true,
                CanConnect = false
            });

            requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Recommendations_ErrorsAreSilent()
        {
            var (service, _, pluginFile) = Service(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            File.WriteAllText(pluginFile, PluginXml("abgelaufen"));

            (await service.GetRecommendationsAsync(Ulberding, new PluginConfiguration(), CancellationToken.None)).Should().BeNull();
        }

        [Fact]
        public async Task Login_StartsDeviceCode_WithOwnApp()
        {
            var bodies = new List<string>();
            var (service, _, _) = Service(request =>
            {
                bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return request.RequestUri!.AbsolutePath == "/oauth/device/code"
                    ? Json("{\"device_code\":\"dc\",\"user_code\":\"ABCD1234\",\"verification_url\":\"https://trakt.tv/activate\",\"expires_in\":600,\"interval\":5}")
                    : new HttpResponseMessage(HttpStatusCode.BadRequest);
            });
            var config = new PluginConfiguration { TraktClientId = "own-id", TraktClientSecret = "own-secret" };

            var status = await service.StartLoginAsync(Ulberding, config, CancellationToken.None);

            status.CanConnect.Should().BeTrue();
            status.UserCode.Should().Be("ABCD1234");
            status.VerificationUrl.Should().Be("https://trakt.tv/activate");
            bodies[0].Should().Contain("own-id").And.NotContain("own-secret");
        }

        [Fact]
        public void PersonalHits_MapOntoLibrary_ByKind()
        {
            var movie = new MediaItemMetadata(Guid.NewGuid(), "Matrix Reloaded", MediaType.Movie) { TmdbId = "604" };
            var show = new MediaItemMetadata(Guid.NewGuid(), "Breaking Bad", MediaType.Series) { TmdbId = "1396" };
            var sameIdOtherKind = new MediaItemMetadata(Guid.NewGuid(), "Film 1396", MediaType.Movie) { TmdbId = "1396" };
            var metadata = new[] { movie, show, sameIdOtherKind }.ToDictionary(m => m.Id);

            var hits = ExternalSimilarityScorer.PersonalHits(new PersonalRecommendations { Movies = new[] { "604" }, Shows = new[] { "999", "1396" } }, metadata);

            hits.Keys.Should().BeEquivalentTo(new[] { movie.Id, show.Id });
            hits[movie.Id].Should().Be(1.0);
            hits[show.Id].Should().BeApproximately(0.985, 0.0001);
        }

        private static (TraktPersonalService Service, List<HttpRequestMessage> Requests, string PluginFile) Service(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var folder = Path.Combine(Path.GetTempPath(), "lr-trakt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var requests = new List<HttpRequestMessage>();
            var pluginFile = Path.Combine(folder, "Trakt.xml");
            var service = new TraktPersonalService(
                NullLogger<TraktPersonalService>.Instance,
                Path.Combine(folder, "trakt-konten.json"),
                pluginFile,
                (request, _) =>
                {
                    requests.Add(request);
                    return Task.FromResult(respond(request));
                })
            {
                PluginClientIdOverride = "plugin-client"
            };
            return (service, requests, pluginFile);
        }

        private static HttpResponseMessage Json(string body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
