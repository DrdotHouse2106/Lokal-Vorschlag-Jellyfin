using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Xunit;
using Part = Jellyfin.Plugin.LocalRecs.Services.CollectionService.CollectionPart;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Filmreihen (nächster Teil) und die Begründung „Weil du … geschaut hast“.
    /// </summary>
    public class CollectionAndReasonTests
    {
        [Fact]
        public void NextPart_IsFirstUnwatchedAfterLastWatched()
        {
            var parts = new[]
            {
                new Part { Title = "Teil 1", Played = true },
                new Part { Title = "Teil 2", Played = true },
                new Part { Title = "Teil 3" },
                new Part { Title = "Teil 4" }
            };

            CollectionService.FindNextPart(parts).Should().Be((2, "Teil 2"));
        }

        [Fact]
        public void NextPart_SkipsStartedOrDislikedParts()
        {
            var parts = new[]
            {
                new Part { Title = "Teil 1", Played = true },
                new Part { Title = "Teil 2", Started = true },
                new Part { Title = "Teil 3", Likes = false },
                new Part { Title = "Teil 4" }
            };

            CollectionService.FindNextPart(parts).Should().Be((3, "Teil 1"));
        }

        [Fact]
        public void NoNextPart_WhenNothingWatched_LastWatchedDisliked_OrAllWatched()
        {
            CollectionService.FindNextPart(new[] { new Part { Title = "A" }, new Part { Title = "B" } }).Should().BeNull();
            CollectionService.FindNextPart(new[] { new Part { Title = "A", Played = true, Likes = false }, new Part { Title = "B" } }).Should().BeNull();
            CollectionService.FindNextPart(new[] { new Part { Title = "A", Played = true }, new Part { Title = "B", Played = true } }).Should().BeNull();
        }

        [Fact]
        public void NextParts_GoToTheFront_AtMostAQuarter_WithoutDuplicates()
        {
            var row = Enumerable.Range(0, 8).Select(i => new ScoredRecommendation(Guid.NewGuid(), 1f - (i * 0.1f))).ToList();
            var inRow = row[5].ItemId;
            var blocked = Guid.NewGuid();
            var extra = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
            var parts = new List<(Guid, string)> { (inRow, "Nächster Teil nach „A“"), (blocked, "x") };
            parts.AddRange(extra.Select(id => (id, "Nächster Teil nach „B“")));

            var result = RecommendationReranker.AddNextParts(row, parts, id => id != blocked, 8);

            result.Should().HaveCount(8);
            result.Take(2).Select(r => r.ItemId).Should().Equal(inRow, extra[0]);
            result[0].Reason.Should().Be("Nächster Teil nach „A“");
            result.Count(r => r.ItemId == inRow).Should().Be(1);
            result.Should().NotContain(r => r.ItemId == blocked);
        }

        [Fact]
        public void Reason_NamesMostSimilarWatchedTitle_OnlyAboveThreshold()
        {
            var watchedAnime = Guid.NewGuid();
            var watchedKrimi = Guid.NewGuid();
            var candidate = Guid.NewGuid();
            var unrelated = Guid.NewGuid();
            var embeddings = new Dictionary<Guid, ItemEmbedding>
            {
                [watchedAnime] = new ItemEmbedding(watchedAnime, new[] { 1f, 0f, 0f }),
                [watchedKrimi] = new ItemEmbedding(watchedKrimi, new[] { 0f, 1f, 0f }),
                [candidate] = new ItemEmbedding(candidate, new[] { 0.9f, 0.1f, 0f }),
                [unrelated] = new ItemEmbedding(unrelated, new[] { 0f, 0f, 1f })
            };
            var metadata = new Dictionary<Guid, MediaItemMetadata>
            {
                [watchedAnime] = new MediaItemMetadata(watchedAnime, "Frieren", MediaType.Series),
                [watchedKrimi] = new MediaItemMetadata(watchedKrimi, "Tatort", MediaType.Series)
            };
            var recs = new[] { new ScoredRecommendation(candidate, 0.9f), new ScoredRecommendation(unrelated, 0.5f) };

            RecommendationExplainer.Explain(recs, new[] { watchedKrimi, watchedAnime }, embeddings, metadata);

            recs[0].Reason.Should().Be("Weil du „Frieren“ geschaut hast");
            recs[1].Reason.Should().BeNull();
        }
    }
}
