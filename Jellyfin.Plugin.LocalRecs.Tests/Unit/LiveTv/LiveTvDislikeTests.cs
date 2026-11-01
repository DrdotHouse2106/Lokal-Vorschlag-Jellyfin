using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Candidate = Jellyfin.Plugin.LocalRecs.LiveTv.LiveTvRecommendationService.ProgramCandidate;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.LiveTv
{
    /// <summary>
    /// Tests für „Weniger davon“ in „Heute im TV für dich“.
    /// </summary>
    public class LiveTvDislikeTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Never_HidesShow_Less_MovesItDown_GenreLess_LowersGenre()
        {
            var programs = new[]
            {
                Program("Tatort: Neuer Fall", "Krimi", 1),
                Program("Polizeiruf 110", "Krimi", 2),
                Program("Die Sendung mit der Maus", "Kinder", 3),
                Program("Quizshow", "Show", 4)
            };

            var none = Rank(LiveTvDislikes.Empty, programs);
            none.Should().Contain(new[] { "tatort", "polizeiruf 110", "die sendung mit der maus" });

            var never = Rank(Dislikes(("tatort", LiveTvDislikes.Never)), programs);
            never.Should().NotContain("tatort");

            var scores = Scores(Dislikes(("polizeiruf 110", LiveTvDislikes.Less)), programs);
            scores["polizeiruf 110"].Should().BeLessThan(scores["tatort"] * 0.5f, "„weniger“ wertet deutlich ab");

            var genre = new LiveTvDislikes(new Dictionary<string, string>(), new HashSet<string> { "krimi" });
            var genreScores = Scores(genre, programs);
            var plain = Scores(LiveTvDislikes.Empty, programs);
            genreScores["tatort"].Should().BeApproximately(plain["tatort"] * LiveTvDislikes.GenreFactor, 0.001f);
            genreScores["die sendung mit der maus"].Should().Be(plain["die sendung mit der maus"]);
        }

        [Fact]
        public void MarkedRegularShow_IsNoLongerForcedFirst()
        {
            var profile = Profile();
            profile.MarkRegular("die sendung mit der maus");
            var ranked = LiveTvRecommendationService.Rank(profile, new[] { Program("Die Sendung mit der Maus", "Kinder", 1) }, new HashSet<string>(), new HashSet<string>(), 10, null, Dislikes(("die sendung mit der maus", LiveTvDislikes.Never)));
            ranked.Should().BeEmpty();
            profile.IsRegular("die sendung mit der maus").Should().BeFalse();
        }

        [Fact]
        public void Store_MarksRemovesAndPersists()
        {
            var path = Path.Combine(Path.GetTempPath(), "lr-livetv-weniger-" + Guid.NewGuid().ToString("N") + ".json");
            var user = Guid.NewGuid();
            var other = Guid.NewGuid();
            try
            {
                var store = new LiveTvFeedbackStore(NullLogger<LiveTvFeedbackStore>.Instance, path);
                store.MarkTitle(user, "tatort", "Tatort", LiveTvDislikes.Less, Now);
                store.MarkTitle(user, "tatort", "Tatort", LiveTvDislikes.Never, Now.AddMinutes(1));
                store.MarkGenre(user, "krimi", "Krimi", Now.AddMinutes(2));

                var reloaded = new LiveTvFeedbackStore(NullLogger<LiveTvFeedbackStore>.Instance, path);
                var dislikes = reloaded.GetDislikes(user);
                dislikes.Titles.Should().Equal(new Dictionary<string, string> { ["tatort"] = LiveTvDislikes.Never }, "die spätere Markierung ersetzt die frühere");
                dislikes.Genres.Should().BeEquivalentTo(new[] { "krimi" });
                reloaded.GetMarks(user).Select(m => m.Kind).Should().Equal(LiveTvDislikes.GenreLess, LiveTvDislikes.Never);
                reloaded.GetDislikes(other).IsEmpty.Should().BeTrue("jeder Benutzer hat eigene Markierungen");

                reloaded.RemoveTitle(user, "tatort").Should().BeTrue();
                reloaded.RemoveGenre(user, "krimi").Should().BeTrue();
                reloaded.RemoveGenre(user, "krimi").Should().BeFalse();
                new LiveTvFeedbackStore(NullLogger<LiveTvFeedbackStore>.Instance, path).GetDislikes(user).IsEmpty.Should().BeTrue();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData(new[] { "Series", "Krimi" }, "Krimi")]
        [InlineData(new[] { "Movie" }, null)]
        [InlineData(new[] { "Dokumentation", "News" }, "Dokumentation")]
        public void MainGenre_SkipsGuideCategories(string[] genres, string? expected)
        {
            LiveTvDislikes.MainGenre(genres).Should().Be(expected);
        }

        private static TvInterestProfile Profile()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Krimi" }, "tatort", null, 1f);
            profile.Add(new[] { "Krimi" }, "polizeiruf 110", null, 1f);
            profile.Add(new[] { "Kinder" }, "die sendung mit der maus", null, 0.8f);
            profile.Normalize();
            return profile;
        }

        private static List<string> Rank(LiveTvDislikes dislikes, IEnumerable<Candidate> programs) =>
            LiveTvRecommendationService.Rank(Profile(), programs, new HashSet<string>(), new HashSet<string>(), 10, null, dislikes).Select(p => p.TitleKey).ToList();

        private static Dictionary<string, float> Scores(LiveTvDislikes dislikes, IEnumerable<Candidate> programs)
        {
            var profile = Profile();
            return programs.ToDictionary(p => p.TitleKey, p => profile.Score(p.Genres, p.TitleKey, p.People, p.Sport) * dislikes.Factor(p.TitleKey, p.Genres));
        }

        private static LiveTvDislikes Dislikes(params (string Title, string Kind)[] titles) =>
            new LiveTvDislikes(titles.ToDictionary(t => t.Title, t => t.Kind), new HashSet<string>());

        private static Candidate Program(string title, string genre, int hours)
        {
            var start = Now.AddHours(hours);
            return new Candidate
            {
                BroadcastKey = title + "|" + start.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                TitleKey = LiveTvText.NormalizeTitle(title),
                Genres = new[] { genre },
                StartDate = start,
                Title = title
            };
        }
    }
}
