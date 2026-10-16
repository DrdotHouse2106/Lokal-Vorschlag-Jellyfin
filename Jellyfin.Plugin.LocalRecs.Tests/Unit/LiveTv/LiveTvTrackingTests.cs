using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.LiveTv
{
    /// <summary>
    /// Tests für das Mitschreiben live geschauter Sendungen.
    /// </summary>
    public class LiveTvTrackingTests : IDisposable
    {
        private static readonly DateTime Saturday = new DateTime(2026, 10, 17, 13, 30, 0, DateTimeKind.Utc);
        private static readonly Guid Sky1 = Guid.NewGuid();
        private static readonly Guid Sky2 = Guid.NewGuid();
        private static readonly Guid Zdf = Guid.NewGuid();

        private readonly string _tempDir;
        private readonly LiveTvWatchStore _store;
        private readonly List<(Guid Channel, LiveTvWatchEntry Program)> _epg = new List<(Guid, LiveTvWatchEntry)>();
        private readonly LiveTvWatchTracker _tracker;
        private readonly Guid _user = Guid.NewGuid();

        public LiveTvTrackingTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString());
            _store = new LiveTvWatchStore(NullLogger<LiveTvWatchStore>.Instance, Path.Combine(_tempDir, "livetv.json"));
            _tracker = new LiveTvWatchTracker(_store, (channel, at) =>
                _epg.Where(e => e.Channel == channel && e.Program.StartDate <= at && e.Program.EndDate > at)
                    .Select(e => e.Program)
                    .FirstOrDefault());

            AddProgram(Sky1, "Fußball: Bundesliga, Bayern - Dortmund", Saturday, 120, "Sport", "Fußball");
            AddProgram(Sky2, "Fußball: Bundesliga, Köln - Bremen", Saturday, 120, "Sport", "Fußball");
            AddProgram(Zdf, "Die Rosenheim-Cops", Saturday, 60, "Krimi");
            AddProgram(Zdf, "heute", Saturday.AddMinutes(60), 15, "Nachrichten");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void Football_SwitchingBetweenChannels_CountsMinutesPerGame()
        {
            // 10 Minuten Sky1, 10 Minuten Sky2, 10 Minuten Sky1, Meldungen alle 2 Minuten
            var t = Saturday;
            Watch(Sky1, ref t, 10);
            Watch(Sky2, ref t, 10);
            Watch(Sky1, ref t, 10);
            _tracker.OnStopped("s", t);

            var entries = _store.GetEntries(_user, 3);
            entries.Should().HaveCount(2);
            entries.Single(e => e.Title.Contains("Bayern", StringComparison.Ordinal)).Minutes.Should().BeApproximately(20, 0.01);
            entries.Single(e => e.Title.Contains("Köln", StringComparison.Ordinal)).Minutes.Should().BeApproximately(10, 0.01);
            entries.Select(LiveTvText.TitleKey).Distinct().Should().Equal("fußball");
        }

        [Fact]
        public void Zapping_BelowMinimum_IsIgnored()
        {
            var t = Saturday;
            Watch(Zdf, ref t, 1);
            Watch(Sky1, ref t, 20);
            _tracker.OnStopped("s", t);

            _store.GetEntries(_user, 3).Select(e => e.Title).Should().Equal("Fußball: Bundesliga, Bayern - Dortmund");
            _store.GetEntries(_user, 0).Should().HaveCount(2);
        }

        [Fact]
        public void ProgramEndsWhileWatching_TimeIsSplitBetweenPrograms()
        {
            var t = Saturday.AddMinutes(56);
            _tracker.OnProgress("s", _user, Zdf, false, t);
            _tracker.OnProgress("s", _user, Zdf, false, t.AddMinutes(4));
            _tracker.OnProgress("s", _user, Zdf, false, t.AddMinutes(8));

            var entries = _store.GetEntries(_user, 0).ToDictionary(e => e.Title, e => e.Minutes);
            entries["Die Rosenheim-Cops"].Should().BeApproximately(4, 0.01);
            entries["heute"].Should().BeApproximately(4, 0.01);
        }

        [Fact]
        public void Paused_IsNotCounted()
        {
            _tracker.OnProgress("s", _user, Zdf, true, Saturday);
            _tracker.OnProgress("s", _user, Zdf, true, Saturday.AddMinutes(4));
            _tracker.OnStopped("s", Saturday.AddMinutes(8));

            _store.GetEntries(_user, 0).Should().BeEmpty();
        }

        [Fact]
        public void LongGapWithoutReports_IsNotCounted()
        {
            _tracker.OnProgress("s", _user, Zdf, false, Saturday);
            _tracker.OnStopped("s", Saturday.AddMinutes(40));

            _store.GetEntries(_user, 0).Should().BeEmpty();
        }

        [Fact]
        public void SwitchingToLibraryContent_StopsTracking()
        {
            _tracker.OnProgress("s", _user, Zdf, false, Saturday);
            _tracker.OnProgress("s", _user, null, false, Saturday.AddMinutes(4));
            _tracker.OnStopped("s", Saturday.AddMinutes(30));

            _store.GetEntries(_user, 0).Single().Minutes.Should().BeApproximately(4, 0.01);
            _tracker.ActiveSessions.Should().Be(0);
        }

        [Fact]
        public void TwoUsersOnSameChannel_AreTrackedSeparately()
        {
            var other = Guid.NewGuid();
            _tracker.OnProgress("a", _user, Zdf, false, Saturday);
            _tracker.OnProgress("b", other, Zdf, false, Saturday);
            _tracker.OnProgress("a", _user, Zdf, false, Saturday.AddMinutes(4));
            _tracker.OnProgress("b", other, Zdf, false, Saturday.AddMinutes(2));

            _store.GetEntries(_user, 0).Single().Minutes.Should().BeApproximately(4, 0.01);
            _store.GetEntries(other, 0).Single().Minutes.Should().BeApproximately(2, 0.01);
        }

        [Fact]
        public void Store_PersistsAndPrunes()
        {
            var t = Saturday;
            Watch(Zdf, ref t, 10);
            _tracker.OnStopped("s", t);
            _store.SaveIfDirty();

            var reloaded = new LiveTvWatchStore(NullLogger<LiveTvWatchStore>.Instance, Path.Combine(_tempDir, "livetv.json"));
            reloaded.GetEntries(_user, 0).Single().Minutes.Should().BeApproximately(10, 0.01);

            reloaded.Prune(Saturday.AddDays(1));
            reloaded.Count.Should().Be(0);
        }

        [Theory]
        [InlineData("Fußball: 1. Bundesliga, 7. Spieltag", "fußball")]
        [InlineData("Tatort: Der Fall Holdt", "tatort")]
        [InlineData("  Die   Simpsons ", "die simpsons")]
        [InlineData("ZDF - heute journal", "zdf")]
        [InlineData("Ab: kurz", "ab: kurz")]
        [InlineData("", "")]
        public void NormalizeTitle_MakesBroadcastsComparable(string title, string expected)
        {
            LiveTvText.NormalizeTitle(title).Should().Be(expected);
        }

        private void Watch(Guid channel, ref DateTime t, int minutes)
        {
            for (var m = 0; m < minutes; m += 2)
            {
                _tracker.OnProgress("s", _user, channel, false, t);
                t = t.AddMinutes(2);
            }
        }

        private void AddProgram(Guid channel, string title, DateTime start, int minutes, params string[] genres)
        {
            _epg.Add((channel, new LiveTvWatchEntry
            {
                BroadcastKey = channel.ToString("N") + "|" + start.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
                Title = title,
                Genres = genres.ToList(),
                StartDate = start,
                EndDate = start.AddMinutes(minutes)
            }));
        }
    }
}
