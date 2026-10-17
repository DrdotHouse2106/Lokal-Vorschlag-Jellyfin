using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Auswertung (empfohlen → geschaut).
    /// </summary>
    public class FeedbackStoreTests : IDisposable
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        private readonly string _path = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid() + ".json");
        private readonly Guid _user = Guid.NewGuid();

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [Fact]
        public void ShownSince_CountsUninterruptedPeriod_AndRestartsAfterGap()
        {
            var store = new FeedbackStore(NullLogger<FeedbackStore>.Instance, _path);
            store.RecordRecommended(_user, FeedbackStore.Movies, new[] { ("a", "A"), ("b", "B") }, Now.AddDays(-30));
            for (var day = 29; day >= 0; day--)
            {
                store.RecordRecommended(_user, FeedbackStore.Movies, new[] { ("a", "A") }, Now.AddDays(-day));
            }

            // b war 30 Tage nicht mehr in der Zeile und kommt jetzt zurück
            store.RecordRecommended(_user, FeedbackStore.Movies, new[] { ("b", "B") }, Now);
            store.RecordWatched(_user, FeedbackStore.Movies, new[] { "c" }, Now);

            var shown = store.GetShownSince(_user, FeedbackStore.Movies, Now);
            shown["a"].Should().Be(Now.AddDays(-30));
            shown["b"].Should().Be(Now, "nach einer Lücke beginnt die Zählung neu");

            store.RecordWatched(_user, FeedbackStore.Movies, new[] { "a" }, Now);
            store.GetShownSince(_user, FeedbackStore.Movies, Now).Should().NotContainKey("a", "geschaute Titel werden nicht abgewertet");
        }

        [Fact]
        public void WatchedRecommendation_CountsAsHitOnce()
        {
            var store = new FeedbackStore(NullLogger<FeedbackStore>.Instance, _path);
            store.RecordRecommended(_user, FeedbackStore.Movies, new[] { ("a", "Film A"), ("b", "Film B"), ("c", "Film C"), ("d", "Film D") }, Now.AddDays(-3));

            store.RecordWatched(_user, FeedbackStore.Movies, new[] { "b" }, Now).Should().BeTrue();
            store.RecordWatched(_user, FeedbackStore.Movies, new[] { "b" }, Now.AddHours(1)).Should().BeFalse("ein zweites Anschauen zählt nicht doppelt");
            store.RecordWatched(_user, FeedbackStore.Movies, new[] { "x" }, Now).Should().BeFalse("nicht empfohlen");
            store.RecordWatched(Guid.NewGuid(), FeedbackStore.Movies, new[] { "a" }, Now).Should().BeFalse("anderer Benutzer");

            var movies = store.GetReport(30, Now).Sections.Single(s => s.Section == FeedbackStore.Movies);
            movies.Recommended.Should().Be(4);
            movies.Watched.Should().Be(1);
            movies.HitRate.Should().Be(25);
            movies.AverageDaysToWatch.Should().Be(3);
        }

        [Fact]
        public void RecommendedAgain_KeepsFirstDate_AndPersists()
        {
            var store = new FeedbackStore(NullLogger<FeedbackStore>.Instance, _path);
            store.RecordRecommended(_user, FeedbackStore.LiveTv, new[] { ("title:tatort", "Tatort") }, Now.AddDays(-2));
            store.RecordRecommended(_user, FeedbackStore.LiveTv, new[] { ("title:tatort", "Tatort") }, Now);
            store.RecordWatched(_user, FeedbackStore.LiveTv, new[] { "sky|123", "title:tatort" }, Now);
            store.SaveIfDirty();

            var reloaded = new FeedbackStore(NullLogger<FeedbackStore>.Instance, _path);
            var report = reloaded.GetReport(30, Now);
            report.Sections.Single(s => s.Section == FeedbackStore.LiveTv).AverageDaysToWatch.Should().Be(2);
            report.RecentHits.Single().Title.Should().Be("Tatort");
        }

        [Fact]
        public void Prune_RemovesOldEntries()
        {
            var store = new FeedbackStore(NullLogger<FeedbackStore>.Instance, _path);
            store.RecordRecommended(_user, FeedbackStore.Series, new[] { ("s", "Serie") }, Now.AddDays(-400));
            store.Prune(Now.AddDays(-365));

            store.GetReport(3650, Now).Sections.Sum(s => s.Recommended).Should().Be(0);
        }
    }
}
