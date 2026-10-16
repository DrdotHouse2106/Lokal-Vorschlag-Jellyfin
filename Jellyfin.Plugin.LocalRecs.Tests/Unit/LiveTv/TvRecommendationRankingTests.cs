using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Xunit;
using Candidate = Jellyfin.Plugin.LocalRecs.LiveTv.LiveTvRecommendationService.ProgramCandidate;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.LiveTv
{
    /// <summary>
    /// Tests für „Heute im TV für dich“: Interessenprofil und Auswahl der Sendungen.
    /// </summary>
    public class TvRecommendationRankingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        private static readonly HashSet<string> NoExclusions = new HashSet<string>();
        private static readonly HashSet<string> NoneWatched = new HashSet<string>();

        [Fact]
        public void FootballFan_GetsUpcomingFootballFirst()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Sport", "Fußball" }, "fußball", null, 1.5f);
            profile.Add(new[] { "Krimi" }, "tatort", null, 0.2f);

            var ranked = LiveTvRecommendationService.Rank(
                profile,
                new[]
                {
                    Program("Kochshow", "Kochen", hours: 1),
                    Program("Fußball: Bundesliga, Leipzig - Mainz", "Sport", hours: 3),
                    Program("Tatort: Neuer Fall", "Krimi", hours: 8)
                },
                NoExclusions,
                NoneWatched,
                10);

            ranked.Select(p => p.TitleKey).Should().Equal("fußball", "tatort");
        }

        [Fact]
        public void FootballFan_GetsNoHandballGolfOrTennis()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Sport" }, "fußball", null, 2f);

            var ranked = LiveTvRecommendationService.Rank(
                profile,
                new[]
                {
                    Program("Handball: Bundesliga, Kiel - Flensburg", "Sport", hours: 1),
                    Program("Golf: Ryder Cup", "Sport", hours: 2),
                    Program("Tennis: Finale", "Sport", hours: 3),
                    Program("Fußball: 2. Bundesliga", "Sport", hours: 4)
                },
                NoExclusions,
                NoneWatched,
                10);

            ranked.Select(p => p.TitleKey).Should().Equal("fußball");
        }

        [Theory]
        [InlineData("Fußball: Champions League", "Manchester City – Paris Saint-Germain", "Fußball", "fußball")]
        [InlineData("Bundesliga 26/27", "1. FC Köln – Borussia M'gladbach", "Sport", "fußball")]
        [InlineData("Estudiantes (ARG) – Flamengo (BRA)", null, "Fußball", "fußball")]
        [InlineData("Bundesliga: Eintracht Frankfurt – 1. FC Köln", null, "Sports", "fußball")]
        [InlineData("Handball: Bundesliga", "HC Elbflorenz – Die Eulen Ludwigshafen", "Handball", "handball")]
        [InlineData("Handball: 2. Bundesliga", "7. Spieltag: SG Flensburg-Handewitt – Füchse Berlin", "Sport", "handball")]
        [InlineData("Golf: Ryder Cup", null, "Sport", "golf")]
        [InlineData("NHL: Bruins – Rangers", null, "Sport", "eishockey")]
        [InlineData("Sportschau", null, "Sportmagazin", null)]
        [InlineData("Die Rosenheim-Cops", "Mord im Golfclub", "Krimiserie", null)]
        [InlineData("Tatort: Bundesliga-Mord", null, "Krimi", null)]
        [InlineData("Fußballmagazin", null, "Fußballmagazin", "fußball")]
        [InlineData("Darts: WM", null, null, "darts")]
        public void DetectSport_RecognizesRealEpgTitles(string title, string? episode, string? genre, string? expected)
        {
            LiveTvText.DetectSport(genre == null ? null : new[] { genre }, title, episode).Should().Be(expected);
        }

        [Fact]
        public void FootballFan_GetsAllFootballGames_FromAllProviders_ButNoHandball()
        {
            // Mitgeschaut: Sky-Konferenz (Titel „Fußball: Bundesliga“, Genre Fußball)
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Fußball" }, "fußball", null, 2f, "fußball");

            var ranked = LiveTvRecommendationService.Rank(
                profile,
                new[]
                {
                    SportProgram("Bundesliga 26/27", "1. FC Köln – Borussia M'gladbach", "Sport", hours: 1),
                    SportProgram("Fußball: Champions League", "Manchester City – Paris Saint-Germain", "Fußball", hours: 2),
                    SportProgram("Fußball: Champions League", "Real Madrid – Inter Mailand", "Fußball", hours: 2),
                    SportProgram("Estudiantes (ARG) – Flamengo (BRA)", null, "Fußball", hours: 3),
                    SportProgram("Handball: Bundesliga", "SG Flensburg-Handewitt – Füchse Berlin", "Handball", hours: 1),
                    SportProgram("Golf: Ryder Cup", null, "Sport", hours: 1)
                },
                NoExclusions,
                NoneWatched,
                10);

            ranked.Should().HaveCount(4);
            ranked.Should().OnlyContain(p => p.Sport == "fußball");
        }

        [Fact]
        public void SameShowOnSeveralDates_OnlyNextAiringIsShown()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Animation" }, "american dad", null, 1f);

            var ranked = LiveTvRecommendationService.Rank(
                profile,
                new[]
                {
                    Program("American Dad", "Animation", hours: 5),
                    Program("American Dad", "Animation", hours: 1),
                    Program("American Dad", "Animation", hours: 3)
                },
                NoExclusions,
                NoneWatched,
                10);

            ranked.Should().ContainSingle().Which.StartDate.Should().Be(Now.AddHours(1));
        }

        [Fact]
        public void ExcludedGenresAndWatchedBroadcasts_AreSkipped()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Nachrichten" }, "tagesschau", null, 1f);
            profile.Add(new[] { "Krimi" }, "tatort", null, 1f);
            var tatort = Program("Tatort", "Krimi", hours: 2);

            var ranked = LiveTvRecommendationService.Rank(
                profile,
                new[] { Program("Tagesschau", "Nachrichten", hours: 1), tatort },
                LiveTvText.ParseList("Nachrichten, Wetter"),
                new HashSet<string> { tatort.BroadcastKey },
                10);

            ranked.Should().BeEmpty();
        }

        [Fact]
        public void EmptyProfile_RecommendsNothing()
        {
            LiveTvRecommendationService.Rank(new TvInterestProfile(), new[] { Program("Film", "Drama", hours: 1) }, NoExclusions, NoneWatched, 10)
                .Should().BeEmpty();
        }

        [Fact]
        public void UnrelatedPrograms_BelowMinimumScore_AreNotShown()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Sport" }, "fußball", null, 1f);

            LiveTvRecommendationService.Rank(profile, new[] { Program("Gartenzeit", "Ratgeber", hours: 1) }, NoExclusions, NoneWatched, 10)
                .Should().BeEmpty();
        }

        [Fact]
        public void Limit_IsRespected()
        {
            var profile = new TvInterestProfile();
            profile.Add(new[] { "Krimi" }, null, null, 1f);

            var programs = Enumerable.Range(1, 10).Select(i => Program("Krimi " + i, "Krimi", hours: i));

            LiveTvRecommendationService.Rank(profile, programs, NoExclusions, NoneWatched, 3).Should().HaveCount(3);
        }

        private static Candidate SportProgram(string title, string? episode, string genre, int hours)
        {
            var start = Now.AddHours(hours);
            var sport = LiveTvText.DetectSport(new[] { genre }, title, episode);
            return new Candidate
            {
                BroadcastKey = title + "|" + episode + "|" + start.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                TitleKey = LiveTvText.NormalizeTitle(title),
                Title = title,
                EpisodeTitle = episode,
                Genres = new[] { genre },
                StartDate = start,
                Sport = sport,
                IsSports = sport != null || LiveTvText.IsSportsGenre(new[] { genre })
            };
        }

        private static Candidate Program(string title, string genre, int hours)
        {
            var start = Now.AddHours(hours);
            return new Candidate
            {
                BroadcastKey = title + "|" + start.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                TitleKey = LiveTvText.NormalizeTitle(title),
                Genres = new[] { genre },
                StartDate = start,
                IsSports = LiveTvText.IsSportsGenre(new[] { genre })
            };
        }
    }
}
