using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Reminders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für Live-TV-Erinnerungen: Kalender (iCal), Serien, Schlüssel und QR-Code.
    /// </summary>
    public class ReminderTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        private static ReminderEntry Program(string id, string series, string? episode, int hours) => new ReminderEntry
        {
            ProgramId = id,
            Name = series,
            SeriesKey = series.ToLowerInvariant(),
            EpisodeTitle = episode,
            ChannelName = "Das Erste",
            StartDate = Now.AddHours(hours),
            EndDate = Now.AddHours(hours + 1)
        };

        [Fact]
        public void Ics_HasEventWithAlarm_EscapesAndFoldsLines()
        {
            var entry = Program("abc", "Tatort", "Der Fall; Teil 1, neu", 2);
            entry.Overview = new string('x', 200) + "\nZeile 2";
            var ics = IcsBuilder.Build(new[] { entry }, "Live-TV – Ben", 10, Now);

            ics.Should().StartWith("BEGIN:VCALENDAR\r\n").And.EndWith("END:VCALENDAR\r\n");
            ics.Should().Contain("UID:abc@localrecs").And.Contain("DTSTART:20261020T140000Z").And.Contain("DTEND:20261020T150000Z");
            ics.Should().Contain("SUMMARY:Das Erste: Tatort – Der Fall\\; Teil 1\\, neu");
            ics.Should().Contain("TRIGGER:-PT10M");
            ics.Split("\r\n").Should().OnlyContain(line => System.Text.Encoding.UTF8.GetByteCount(line) <= 75, "Zeilen werden nach 75 Bytes umgebrochen");
            ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Should().Contain("\\nZeile 2");
        }

        [Fact]
        public void Ics_WithoutAlarm_HasNoValarm()
        {
            IcsBuilder.Build(new[] { Program("a", "X", null, 1) }, "K", 0, Now).Should().NotContain("VALARM");
        }

        [Fact]
        public void Merge_AddsSeriesBroadcasts_DeduplicatesEpisodes_KeepsSingles()
        {
            var single = Program("p1", "Tatort", "Folge A", 1);
            var upcoming = new[]
            {
                Program("p1", "Tatort", "Folge A", 1),
                Program("p2", "Tatort", "Folge A", 30),
                Program("p3", "Tatort", "Folge B", 5),
                Program("p4", "Tagesschau", null, 2),
                Program("p5", "Tagesschau", null, 26),
                Program("p6", "Anderes", "X", 3)
            };

            var merged = ReminderService.Merge(new[] { single }, new[] { "tatort", "tagesschau" }, upcoming);

            merged.Select(m => m.ProgramId).Should().Equal("p1", "p4", "p3", "p5");
            merged.Single(m => m.ProgramId == "p1").IsSeries.Should().BeFalse();
            merged.Where(m => m.ProgramId != "p1").Should().OnlyContain(m => m.IsSeries);
        }

        [Fact]
        public void Store_KeepsProgramsAndSeries_TokenFindsUser_RenewInvalidatesOld()
        {
            var path = Path.Combine(Path.GetTempPath(), "lr-reminders-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var user = Guid.NewGuid();
                var store = new ReminderStore(NullLogger<ReminderStore>.Instance, path);
                store.AddProgram(user, Program("p1", "Tatort", null, 1), Now);
                store.AddProgram(user, Program("old", "Alt", null, -100), Now);
                store.AddSeries(user, "tatort", "Tatort", Now);
                var token = store.GetToken(user, false);

                var reloaded = new ReminderStore(NullLogger<ReminderStore>.Instance, path);
                reloaded.GetPrograms(user, Now).Select(p => p.ProgramId).Should().Equal("p1");
                reloaded.GetSeries(user).Single().Key.Should().Be("tatort");
                reloaded.FindUserByToken(token).Should().Be(user);
                reloaded.FindUserByToken("x").Should().BeNull();

                var renewed = reloaded.GetToken(user, true);
                renewed.Should().NotBe(token);
                reloaded.FindUserByToken(token).Should().BeNull("der alte Link gilt nicht mehr");
                reloaded.RemoveProgram(user, "p1").Should().BeTrue();
                reloaded.RemoveProgram(user, "p1").Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void QrCode_PicksSmallVersion_HasFinderPatterns_AndSvg()
        {
            var qr = QrCode.Encode("webcal://jellyfin.example.org/LocalRecs/Calendar/0123456789abcdef0123456789abcdef0123456789abcdef.ics");

            qr.Version.Should().BeInRange(5, 7);
            qr.Size.Should().Be((qr.Version * 4) + 17);
            foreach (var (x, y) in new[] { (0, 0), (qr.Size - 7, 0), (0, qr.Size - 7) })
            {
                qr.GetModule(x, y).Should().BeTrue();
                qr.GetModule(x + 1, y + 1).Should().BeFalse();
                qr.GetModule(x + 3, y + 3).Should().BeTrue();
            }

            qr.ToSvg().Should().StartWith("<?xml").And.Contain("<path d=\"M");
        }

        [Fact]
        public void Ics_EmptyCalendar_HasPlaceholderEvent_ForIos()
        {
            var ics = IcsBuilder.Build(Array.Empty<ReminderEntry>(), "Live-TV – Ben", 10, Now);

            ics.Should().Contain("BEGIN:VEVENT").And.Contain("UID:platzhalter@localrecs").And.Contain("DTSTART;VALUE=DATE:20000101");
            ics.Should().NotContain("VALARM").And.NotContain("X-WR-TIMEZONE");
            IcsBuilder.Build(new[] { Program("a", "X", null, 1) }, "K", 0, Now).Should().NotContain("platzhalter");
        }

        [Fact]
        public void CalendarPage_EncodesLinks_AndOffersWebcalButton()
        {
            var html = CalendarPage.Build("webcal://tv.example.org/LocalRecs/Calendar/ab.ics", "https://tv.example.org/LocalRecs/Calendar/ab.ics?x=1&y=<2>", "Live-TV – \"Ben\"");

            html.Should().Contain("href=\"webcal://tv.example.org/LocalRecs/Calendar/ab.ics\"");
            html.Should().Contain("x=1&amp;y=&lt;2&gt;").And.NotContain("<2>");
            html.Should().Contain("&quot;Ben&quot;").And.Contain("Im Kalender abonnieren");
        }
    }
}
