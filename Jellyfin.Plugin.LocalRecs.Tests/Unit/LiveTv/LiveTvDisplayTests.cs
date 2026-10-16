using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.LiveTv
{
    /// <summary>
    /// Tests für die Sendezeit auf den Karten von „Heute im TV für dich“.
    /// </summary>
    public class LiveTvDisplayTests
    {
        private static readonly TimeZoneInfo Berlin = LiveTvDisplay.ResolveTimeZone("Europe/Berlin");

        // Freitag, 16.10.2026, 14:56 in Berlin (Sommerzeit, UTC+2)
        private static readonly DateTime Now = new DateTime(2026, 10, 16, 12, 56, 0, DateTimeKind.Utc);

        [Fact]
        public void Running_ShowsEndTime()
        {
            LiveTvDisplay.TimeLine(Now.AddMinutes(-41), Now.AddMinutes(4), "SKY ONE", Now, Berlin)
                .Should().Be("Läuft bis 15:00 · SKY ONE");
        }

        [Fact]
        public void LaterToday_TomorrowAndWeekday()
        {
            LiveTvDisplay.TimeLine(new DateTime(2026, 10, 16, 18, 15, 0, DateTimeKind.Utc), null, "SKY KRIMI", Now, Berlin)
                .Should().Be("Heute 20:15 · SKY KRIMI");
            LiveTvDisplay.TimeLine(new DateTime(2026, 10, 17, 1, 50, 0, DateTimeKind.Utc), null, "HR", Now, Berlin)
                .Should().Be("Morgen 03:50 · HR");
            LiveTvDisplay.TimeLine(new DateTime(2026, 10, 18, 11, 0, 0, DateTimeKind.Utc), null, null, Now, Berlin)
                .Should().Be("So 13:00");
        }

        [Fact]
        public void MidnightInBerlin_IsTomorrowEvenIfStillTodayInUtc()
        {
            // 22:30 UTC = 00:30 in Berlin am nächsten Tag
            LiveTvDisplay.TimeLine(new DateTime(2026, 10, 16, 22, 30, 0, DateTimeKind.Utc), null, "ARD", Now, Berlin)
                .Should().Be("Morgen 00:30 · ARD");
        }

        [Fact]
        public void Title_PutsPairingFirstForSports()
        {
            LiveTvDisplay.Title("Fußball: Champions League", "Manchester City – Paris Saint-Germain", true)
                .Should().Be("Manchester City – Paris Saint-Germain · Fußball: Champions League");
            LiveTvDisplay.Title("The Rookie", "Das Feuer", false).Should().Be("The Rookie: Das Feuer");
            LiveTvDisplay.Title("Tatort", null, false).Should().Be("Tatort");
            LiveTvDisplay.Title("Tatort: Das Feuer", "Das Feuer", false).Should().Be("Tatort: Das Feuer");
        }

        [Fact]
        public void UnknownTimeZone_FallsBackToServerTime()
        {
            LiveTvDisplay.ResolveTimeZone("Mars/Olympus").Should().Be(TimeZoneInfo.Local);
        }
    }
}
