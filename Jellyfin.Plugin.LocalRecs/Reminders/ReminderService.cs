using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Erinnerungen an Live-TV-Sendungen: einzelne Sendungen und ganze Serien (alle künftigen Ausstrahlungen
    /// mit gleichem Titel, Folgen ohne Doppelte). Daraus entsteht der Kalender pro Benutzer.
    /// </summary>
    public class ReminderService
    {
        private static readonly TimeSpan SeriesLookahead = TimeSpan.FromDays(14);
        private static readonly TimeSpan KeepAfterEnd = TimeSpan.FromDays(2);

        private readonly ILibraryManager _libraryManager;
        private readonly ILiveTvManager _liveTvManager;
        private readonly ReminderStore _store;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReminderService"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="liveTvManager">Live TV manager.</param>
        /// <param name="store">Reminder store.</param>
        public ReminderService(ILibraryManager libraryManager, ILiveTvManager liveTvManager, ReminderStore store)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _liveTvManager = liveTvManager ?? throw new ArgumentNullException(nameof(liveTvManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Gets the store.</summary>
        public ReminderStore Store => _store;

        /// <summary>
        /// Builds the series key of a programme (normalized series name or title).
        /// </summary>
        /// <param name="seriesName">Series name.</param>
        /// <param name="name">Title.</param>
        /// <returns>The key.</returns>
        public static string SeriesKey(string? seriesName, string? name)
        {
            return LiveTvText.NormalizeTitle(string.IsNullOrWhiteSpace(seriesName) ? name : seriesName);
        }

        /// <summary>
        /// Merges single reminders and broadcasts of remembered series. Series broadcasts with the same episode title are
        /// listed once (earliest); without episode title every broadcast counts.
        /// </summary>
        /// <param name="singles">Remembered programmes.</param>
        /// <param name="seriesKeys">Remembered series keys.</param>
        /// <param name="upcoming">Upcoming programmes from the guide.</param>
        /// <returns>All entries by start.</returns>
        public static List<ReminderEntry> Merge(IEnumerable<ReminderEntry> singles, IEnumerable<string> seriesKeys, IEnumerable<ReminderEntry> upcoming)
        {
            var result = singles.Select(s => s.Clone()).ToList();
            var keys = new HashSet<string>(seriesKeys, StringComparer.Ordinal);
            var known = new HashSet<string>(result.Select(r => r.ProgramId), StringComparer.Ordinal);
            var episodes = new HashSet<string>(
                result.Where(r => r.SeriesKey != null && LiveTvText.NormalizeFeature(r.EpisodeTitle).Length > 0)
                    .Select(r => r.SeriesKey + "|" + LiveTvText.NormalizeFeature(r.EpisodeTitle)),
                StringComparer.Ordinal);
            foreach (var program in upcoming.Where(p => p.SeriesKey != null && keys.Contains(p.SeriesKey)).OrderBy(p => p.StartDate))
            {
                if (known.Contains(program.ProgramId))
                {
                    continue;
                }

                var episode = LiveTvText.NormalizeFeature(program.EpisodeTitle);
                if (episode.Length > 0 && !episodes.Add(program.SeriesKey + "|" + episode))
                {
                    continue;
                }

                var copy = program.Clone();
                copy.IsSeries = true;
                result.Add(copy);
                known.Add(copy.ProgramId);
            }

            return result.OrderBy(r => r.StartDate).ToList();
        }

        /// <summary>
        /// Creates a snapshot of a programme.
        /// </summary>
        /// <param name="program">The programme.</param>
        /// <returns>The entry.</returns>
        public ReminderEntry Snapshot(LiveTvProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);
            var channel = program.ChannelId == Guid.Empty ? null : _libraryManager.GetItemById(program.ChannelId);
            return new ReminderEntry
            {
                ProgramId = program.Id.ToString("N"),
                ChannelId = program.ChannelId == Guid.Empty ? null : program.ChannelId.ToString("N"),
                ChannelName = channel?.Name,
                SeriesKey = SeriesKey(program.SeriesName, program.Name),
                Name = program.Name ?? string.Empty,
                EpisodeTitle = program.EpisodeTitle,
                Overview = program.Overview,
                StartDate = program.StartDate,
                EndDate = program.EndDate ?? program.StartDate.AddMinutes(30)
            };
        }

        /// <summary>
        /// Gets a programme of the guide.
        /// </summary>
        /// <param name="programId">Programme ID.</param>
        /// <returns>The programme, or null.</returns>
        public LiveTvProgram? GetProgram(Guid programId)
        {
            return _libraryManager.GetItemById(programId) as LiveTvProgram;
        }

        /// <summary>
        /// Gets all reminders of a user (single programmes and broadcasts of remembered series).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="now">Current time (UTC).</param>
        /// <returns>The entries by start.</returns>
        public List<ReminderEntry> GetEntries(User user, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(user);
            var singles = _store.GetPrograms(user.Id, now);
            var series = _store.GetSeries(user.Id);
            var upcoming = series.Count == 0 ? new List<ReminderEntry>() : GetUpcoming(user, now);
            return Merge(singles, series.Select(s => s.Key), upcoming)
                .Where(e => e.EndDate >= now - KeepAfterEnd)
                .ToList();
        }

        private List<ReminderEntry> GetUpcoming(User user, DateTime now)
        {
            var folder = _liveTvManager.GetInternalLiveTvFolder(CancellationToken.None);
            return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.LiveTvProgram },
                MinEndDate = now,
                MaxStartDate = now + SeriesLookahead,
                TopParentIds = new[] { folder.Id }
            }).OfType<LiveTvProgram>().Select(Snapshot).ToList();
        }
    }
}
