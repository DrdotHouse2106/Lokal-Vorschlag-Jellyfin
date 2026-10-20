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
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die öffentlichen Ähnlichkeitslisten (TMDB, Trakt).
    /// </summary>
    public class ExternalSimilarityTests
    {
        private static readonly Guid Matrix = Guid.NewGuid();
        private static readonly Guid Reloaded = Guid.NewGuid();
        private static readonly Guid Inception = Guid.NewGuid();
        private static readonly Guid Notebook = Guid.NewGuid();
        private static readonly Guid Breaking = Guid.NewGuid();

        [Fact]
        public void ParseTmdb_ReadsIdsInOrder()
        {
            var ids = SimilarityParser.ParseTmdb("{\"page\":1,\"results\":[{\"id\":604,\"title\":\"x\"},{\"id\":27205},{\"name\":\"ohne id\"}]}");

            ids.Should().Equal("604", "27205");
        }

        [Fact]
        public void ParseTrakt_ReadsTmdbIds_SkipsEntriesWithout()
        {
            var ids = SimilarityParser.ParseTrakt("[{\"title\":\"a\",\"ids\":{\"trakt\":1,\"tmdb\":604}},{\"ids\":{\"trakt\":2,\"tmdb\":null}},{\"ids\":{\"tmdb\":157336}}]");

            ids.Should().Equal("604", "157336");
        }

        [Fact]
        public void Merge_StrongestMentionCounts_FurtherSourcesAddALittle_SelfIsSkipped()
        {
            var merged = SimilarityParser.Merge(
                "movie",
                "603",
                new[]
                {
                    ((IReadOnlyList<string>)new[] { "604", "603", "11", "12", "13", "27205" }, SimilarityParser.TmdbRecommendationsWeight),
                    ((IReadOnlyList<string>)new[] { "27205" }, SimilarityParser.TmdbSimilarWeight)
                });

            merged.Select(m => m.TmdbId).Should().Equal("604", "27205", "11", "12", "13");
            merged[0].Weight.Should().BeApproximately(1.0, 0.0001);
            // Rang 6 in den Empfehlungen (0,8) plus ein Viertel der zweiten Nennung (0,6 × 1 × 0,25)
            merged[1].Weight.Should().BeApproximately(0.8 + 0.15, 0.0001);
            merged.Should().OnlyContain(m => m.Kind == "movie");
        }

        [Fact]
        public void Scorer_BoostsLibraryTitlesNamedAsSimilar_WithSeedForReason()
        {
            var metadata = Library();
            var lists = new Dictionary<string, IReadOnlyList<SimilarTitle>>
            {
                ["movie:603"] = new List<SimilarTitle>
                {
                    new SimilarTitle { Kind = "movie", TmdbId = "604", Weight = 1.0 },
                    new SimilarTitle { Kind = "movie", TmdbId = "27205", Weight = 0.5 },
                    new SimilarTitle { Kind = "movie", TmdbId = "999999", Weight = 1.0 }
                }
            };

            var hits = ExternalSimilarityScorer.Compute(new[] { (Matrix, 1.0) }, metadata, (kind, id) => lists.GetValueOrDefault(kind + ":" + id));

            hits.Keys.Should().BeEquivalentTo(new[] { Reloaded, Inception });
            hits[Reloaded].Strength.Should().BeApproximately(1 - Math.Exp(-1), 0.0001);
            hits[Reloaded].SeedId.Should().Be(Matrix);
            hits[Inception].Strength.Should().BeLessThan(hits[Reloaded].Strength);
        }

        [Fact]
        public void Scorer_KeepsKindsApart_AndNeverBoostsSeeds()
        {
            var metadata = Library();
            var lists = new Dictionary<string, IReadOnlyList<SimilarTitle>>
            {
                // Serie 1396 heißt bei TMDB als Film etwas anderes – nur "tv:1396" darf treffen
                ["movie:603"] = new List<SimilarTitle> { new SimilarTitle { Kind = "movie", TmdbId = "1396", Weight = 1 } },
                ["tv:1396"] = new List<SimilarTitle> { new SimilarTitle { Kind = "movie", TmdbId = "603", Weight = 1 } }
            };

            var hits = ExternalSimilarityScorer.Compute(new[] { (Matrix, 1.0), (Breaking, 1.0) }, metadata, (kind, id) => lists.GetValueOrDefault(kind + ":" + id));

            hits.Should().BeEmpty();
        }

        [Fact]
        public void WithBonus_AddsBonus_KeepsReason_DoublesInColdStart()
        {
            var bonus = new Dictionary<Guid, double> { [Reloaded] = 0.2 };

            var boosted = RecommendationEngine.WithBonus(new ScoredRecommendation(Reloaded, 0.5f) { Reason = "x" }, bonus);
            var untouched = RecommendationEngine.WithBonus(new ScoredRecommendation(Notebook, 0.5f), bonus);

            boosted.Score.Should().BeApproximately(0.7f, 0.0001f);
            boosted.Reason.Should().Be("x");
            untouched.Score.Should().Be(0.5f);
            RecommendationEngine.WithBonus(new ScoredRecommendation(Reloaded, 0.5f), bonus, 2).Score.Should().BeApproximately(0.9f, 0.0001f);
        }

        [Fact]
        public async Task Refresh_FetchesTmdbAndTrakt_SendsOnlyTitleIds_AndCaches()
        {
            var requests = new List<HttpRequestMessage>();
            var service = Service(requests, request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/recommendations", StringComparison.Ordinal))
                {
                    return Json("{\"results\":[{\"id\":604}]}");
                }

                if (path.EndsWith("/similar", StringComparison.Ordinal))
                {
                    return Json("{\"results\":[{\"id\":27205}]}");
                }

                return Json("[{\"ids\":{\"tmdb\":604}}]");
            });
            var config = new PluginConfiguration { TmdbApiKey = "abc123", TraktClientId = "trakt-id" };
            var matrix = Library()[Matrix];

            var first = await service.RefreshAsync(new[] { matrix }, config, CancellationToken.None);
            var second = await service.RefreshAsync(new[] { matrix }, config, CancellationToken.None);

            first.Fetched.Should().Be(1);
            first.Requests.Should().Be(3);
            second.Requests.Should().Be(0, "die Liste ist 7 Tage im Zwischenspeicher");
            service.Lookup("movie", "603")!.Select(t => t.TmdbId).Should().Equal("604", "27205");

            requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path)).Should().Equal(
                "https://api.themoviedb.org/3/movie/603/recommendations",
                "https://api.themoviedb.org/3/movie/603/similar",
                "https://api.trakt.tv/movies/tt0133093/related");
            requests[0].RequestUri!.Query.Should().Contain("api_key=abc123");
            requests[2].Headers.GetValues("trakt-api-key").Should().Equal("trakt-id");
            requests[2].RequestUri!.Query.Should().NotContain("abc123");
        }

        [Fact]
        public async Task Refresh_UsesBearerForLongTmdbTokens()
        {
            var requests = new List<HttpRequestMessage>();
            var service = Service(requests, _ => Json("{\"results\":[]}"));
            var token = new string('x', 120);

            await service.RefreshAsync(new[] { Library()[Matrix] }, new PluginConfiguration { TmdbApiKey = token }, CancellationToken.None);

            requests.Should().HaveCount(2);
            requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
            requests[0].RequestUri!.Query.Should().NotContain("api_key");
        }

        [Fact]
        public async Task Refresh_NotFound_IsCachedAsEmpty_RejectedKey_StopsSource()
        {
            var requests = new List<HttpRequestMessage>();
            var service = Service(requests, request => request.RequestUri!.AbsolutePath.Contains("/603/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var library = Library();

            var result = await service.RefreshAsync(new[] { library[Matrix], library[Reloaded], library[Inception] }, new PluginConfiguration { TmdbApiKey = "k" }, CancellationToken.None);

            service.Lookup("movie", "603").Should().BeEmpty();
            service.Lookup("movie", "604").Should().BeNull();
            result.Fetched.Should().Be(1);
            requests.Should().HaveCount(3, "nach dem abgelehnten Schlüssel keine weiteren TMDB-Anfragen");
        }

        [Fact]
        public async Task Refresh_RetriesOnceAfterTooManyRequests()
        {
            var calls = 0;
            var service = Service(new List<HttpRequestMessage>(), _ =>
            {
                calls++;
                return calls == 1 ? new HttpResponseMessage((HttpStatusCode)429) : Json("{\"results\":[{\"id\":604}]}");
            });

            var result = await service.RefreshAsync(new[] { Library()[Matrix] }, new PluginConfiguration { TmdbApiKey = "k" }, CancellationToken.None);

            result.Fetched.Should().Be(1);
            calls.Should().Be(3);
        }

        [Fact]
        public async Task Refresh_RespectsMaxLookupsPerRun_AndSkipsWithoutKeys()
        {
            var requests = new List<HttpRequestMessage>();
            var service = Service(requests, _ => Json("{\"results\":[]}"));
            var library = Library();

            var none = await service.RefreshAsync(library.Values, new PluginConfiguration(), CancellationToken.None);
            var limited = await service.RefreshAsync(library.Values, new PluginConfiguration { TmdbApiKey = "k", ExternalSimilarityMaxLookupsPerRun = 2 }, CancellationToken.None);

            // Ohne eigenen Schlüssel greift der von Jellyfins TMDb-Plugin – im Test ist das nicht geladen
            none.Skipped.Should().NotBeNull();
            limited.Fetched.Should().Be(2);
            limited.Pending.Should().Be(0);
        }

        private static ExternalSimilarityService Service(List<HttpRequestMessage> requests, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var cache = Path.Combine(Path.GetTempPath(), "lr-similar-" + Guid.NewGuid().ToString("N") + ".json");
            return new ExternalSimilarityService(
                NullLogger<ExternalSimilarityService>.Instance,
                cache,
                (request, _) =>
                {
                    requests.Add(request);
                    return Task.FromResult(respond(request));
                },
                (_, _) => Task.CompletedTask);
        }

        private static HttpResponseMessage Json(string body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

        private static Dictionary<Guid, MediaItemMetadata> Library()
        {
            MediaItemMetadata Item(Guid id, string name, MediaType type, string tmdb, string? imdb = null)
                => new MediaItemMetadata(id, name, type) { TmdbId = tmdb, ImdbId = imdb };

            return new[]
            {
                Item(Matrix, "Matrix", MediaType.Movie, "603", "tt0133093"),
                Item(Reloaded, "Matrix Reloaded", MediaType.Movie, "604"),
                Item(Inception, "Inception", MediaType.Movie, "27205"),
                Item(Notebook, "Wie ein einziger Tag", MediaType.Movie, "11036"),
                Item(Breaking, "Breaking Bad", MediaType.Series, "1396")
            }.ToDictionary(i => i.Id);
        }
    }
}
