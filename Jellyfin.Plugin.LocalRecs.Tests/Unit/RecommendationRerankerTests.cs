using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für den Feinschliff der Empfehlungen: ignorierte Titel, Genre-Vielfalt, abgebrochene Filme.
    /// </summary>
    public class RecommendationRerankerTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(5, 14, 1f)]
        [InlineData(14, 14, 1f)]
        [InlineData(24, 14, 0.8f)]
        [InlineData(100, 14, 0.5f)]
        [InlineData(100, 0, 1f)]
        public void IgnoredFactor_StartsAfterGraceDays_AndStopsAtHalf(double days, int grace, float expected)
        {
            RecommendationReranker.IgnoredFactor(days, grace).Should().BeApproximately(expected, 0.001f);
        }

        [Fact]
        public void LongIgnoredTitle_MovesBehindFreshOnes()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var metadata = Metadata((a, "Drama"), (b, "Komödie"));
            var scored = new[] { new ScoredRecommendation(a, 0.9f), new ScoredRecommendation(b, 0.7f) };

            var result = RecommendationReranker.Rerank(scored, metadata, new Dictionary<Guid, DateTime> { [a] = Now.AddDays(-40) }, Now, 14, 10);

            result.Select(r => r.ItemId).Should().Equal(b, a);
        }

        [Fact]
        public void AtMostAThirdFromOneGenre_RestIsFilledUp()
        {
            var items = Enumerable.Range(0, 12).Select(i => (Id: Guid.NewGuid(), Genre: i < 10 ? "Action" : "Krimi")).ToList();
            var metadata = Metadata(items.ToArray());
            var scored = items.Select((x, i) => new ScoredRecommendation(x.Id, 1f - (i * 0.01f)));

            var result = RecommendationReranker.Rerank(scored, metadata, null, Now, 14, 6);
            result.Should().HaveCount(6);
            result.Count(r => metadata[r.ItemId].Genres[0] == "Krimi").Should().Be(2);
            result.Count(r => metadata[r.ItemId].Genres[0] == "Action").Should().Be(4, "zwei Action-Titel nach Obergrenze, dann aufgefüllt");

            // Nur ein Genre vorhanden: Zeile wird trotzdem voll
            RecommendationReranker.Rerank(scored.Take(10), metadata, null, Now, 14, 6).Should().HaveCount(6);
        }

        [Theory]
        [InlineData(10, 100, 10, true)]
        [InlineData(10, 100, 1, false)]
        [InlineData(50, 100, 10, false)]
        [InlineData(0, 100, 10, false)]
        public void AbandonedMovie_IsStoppedEarlyAndNotResumed(long position, long runtime, int daysAgo, bool expected)
        {
            UserProfileService.IsAbandoned(position, runtime, Now.AddDays(-daysAgo), Now).Should().Be(expected);
        }

        private static Dictionary<Guid, MediaItemMetadata> Metadata(params (Guid Id, string Genre)[] items)
        {
            return items.ToDictionary(x => x.Id, x =>
            {
                var m = new MediaItemMetadata(x.Id, "Titel", MediaType.Movie);
                m.AddGenre(x.Genre);
                return m;
            });
        }
    }
}
