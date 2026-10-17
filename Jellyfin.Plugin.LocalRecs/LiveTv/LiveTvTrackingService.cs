using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Empfängt die Wiedergabe-Meldungen aller Clients (auch der TV-Apps) und schreibt mit,
    /// welche Live-TV-Sendungen ein Benutzer wie lange geschaut hat.
    /// </summary>
    public sealed class LiveTvTrackingService : IHostedService, IDisposable
    {
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);

        private readonly ILogger<LiveTvTrackingService> _logger;
        private readonly ISessionManager _sessionManager;
        private readonly ILibraryManager _libraryManager;
        private readonly LiveTvWatchStore _store;
        private readonly FeedbackStore _feedback;
        private readonly XmltvSource _xmltv;
        private readonly LiveTvWatchTracker _tracker;
        private readonly ConcurrentDictionary<Guid, LiveTvWatchEntry> _currentPrograms = new ConcurrentDictionary<Guid, LiveTvWatchEntry>();
        private Timer? _timer;
        private DateTime _lastSave = DateTime.UtcNow;
        private DateTime _lastPrune = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvTrackingService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionManager">Session manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="store">Store for the watched minutes.</param>
        /// <param name="feedback">Evaluation of the recommendations.</param>
        /// <param name="xmltv">Additional XMLTV source for people.</param>
        public LiveTvTrackingService(
            ILogger<LiveTvTrackingService> logger,
            ISessionManager sessionManager,
            ILibraryManager libraryManager,
            LiveTvWatchStore store,
            FeedbackStore feedback,
            XmltvSource xmltv)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            _xmltv = xmltv ?? throw new ArgumentNullException(nameof(xmltv));
            _tracker = new LiveTvWatchTracker(store, ResolveProgram);
        }

        /// <summary>
        /// Gets the number of live TV playbacks seen at the last check (for the status page).
        /// </summary>
        public static int ActiveLiveTvSessions { get; private set; }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart += OnPlaybackProgress;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _timer = new Timer(_ => DoMaintenance(), null, MaintenanceInterval, MaintenanceInterval);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart -= OnPlaybackProgress;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _store.SaveIfDirty();
            _feedback.SaveIfDirty();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _timer?.Dispose();
        }

        private static Guid? GetChannelId(BaseItem? item)
        {
            return item switch
            {
                LiveTvChannel channel => channel.Id,
                LiveTvProgram program => program.ChannelId,
                _ => null
            };
        }

        private static string? ClientName(SessionInfo? session, string? clientName, string? deviceName)
        {
            var client = clientName ?? session?.Client;
            var device = deviceName ?? session?.DeviceName;
            if (string.IsNullOrWhiteSpace(device))
            {
                return client;
            }

            return string.IsNullOrWhiteSpace(client) ? device : client + " (" + device + ")";
        }

        private static string SessionKey(PlaybackProgressEventArgs e, Guid userId)
        {
            var session = e.Session?.Id ?? e.DeviceId ?? e.PlaySessionId ?? string.Empty;
            return session + "|" + userId.ToString("N");
        }

        private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var channelId = GetChannelId(e.Item);
                var now = DateTime.UtcNow;
                var seen = new LiveTvDiagnostics.PlaybackSeen
                {
                    Time = now,
                    Source = "Ereignis",
                    Client = ClientName(e.Session, e.ClientName, e.DeviceName),
                    UserName = e.Users == null ? null : string.Join(", ", e.Users.Select(u => u.Username)),
                    ItemType = e.Item?.GetType().Name ?? e.Session?.NowPlayingItem?.Type.ToString(),
                    ItemName = e.Item?.Name ?? e.Session?.NowPlayingItem?.Name,
                    IsLiveTv = channelId != null
                };
                if (LiveTvDiagnostics.Record(seen, true))
                {
                    _logger.LogInformation("Live-TV erkannt: {Channel} auf {Client} ({User})", seen.ItemName, seen.Client, seen.UserName);
                }

                if (!Config.EnableLiveTvTracking || e.Users == null)
                {
                    return;
                }

                foreach (var user in e.Users)
                {
                    _tracker.OnProgress(SessionKey(e, user.Id), user.Id, channelId, e.IsPaused, now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Live-TV-Wiedergabe konnte nicht erfasst werden");
            }
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            try
            {
                if (e.Users == null)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                foreach (var user in e.Users)
                {
                    _tracker.OnStopped(SessionKey(e, user.Id), now);
                }

                RecordLibraryHit(e, now);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ende der Live-TV-Wiedergabe konnte nicht erfasst werden");
            }
        }

        /// <summary>
        /// Evaluation: a recommended movie or series was watched (finished or at least 10 minutes).
        /// </summary>
        private void RecordLibraryHit(PlaybackStopEventArgs e, DateTime now)
        {
            var watchedEnough = e.PlayedToCompletion || (e.PlaybackPositionTicks ?? 0) >= TimeSpan.FromMinutes(10).Ticks;
            if (!watchedEnough)
            {
                return;
            }

            var (section, key) = e.Item switch
            {
                MediaBrowser.Controller.Entities.Movies.Movie movie => (FeedbackStore.Movies, movie.Id.ToString("N")),
                MediaBrowser.Controller.Entities.TV.Episode episode => (FeedbackStore.Series, episode.SeriesId.ToString("N")),
                _ => (null, null)
            };

            if (section == null)
            {
                return;
            }

            foreach (var user in e.Users)
            {
                _feedback.RecordWatched(user.Id, section, new[] { key }, now);
            }
        }

        private LiveTvWatchEntry? ResolveProgram(Guid channelId, DateTime at)
        {
            if (_currentPrograms.TryGetValue(channelId, out var cached)
                && cached.StartDate <= at
                && (cached.EndDate == null || cached.EndDate > at))
            {
                return cached;
            }

            var program = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.LiveTvProgram },
                ChannelIds = new[] { channelId },
                MaxStartDate = at,
                MinEndDate = at,
                OrderBy = new[] { (ItemSortBy.StartDate, SortOrder.Descending) },
                Limit = 1
            }).OfType<LiveTvProgram>().FirstOrDefault();

            LiveTvDiagnostics.RecordProgramLookup(program != null, DateTime.UtcNow);
            if (program == null)
            {
                _logger.LogDebug("Keine laufende Sendung im Programmführer für Kanal {ChannelId} um {Time}", channelId, at);
                return null;
            }

            // Jellyfin speichert für EPG-Sendungen keine Personen – dann aus der XMLTV-Quelle ergänzen
            var people = _libraryManager.GetPeople(program).Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Take(10).ToList();
            var xmltv = _xmltv.Lookup(program.StartDate, program.Name, program.EpisodeTitle);
            if (people.Count == 0 && xmltv != null)
            {
                people = xmltv.People.ToList();
            }

            var entry = new LiveTvWatchEntry
            {
                BroadcastKey = channelId.ToString("N") + "|" + program.StartDate.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
                Title = program.Name ?? string.Empty,
                SeriesName = string.IsNullOrWhiteSpace(program.SeriesName) ? null : program.SeriesName,
                EpisodeTitle = program.EpisodeTitle,
                Genres = program.Genres?.ToList() ?? new System.Collections.Generic.List<string>(),
                People = people,
                ChannelName = _libraryManager.GetItemById(channelId)?.Name,
                IsMovie = program.IsMovie,
                IsSports = program.IsSports,
                ProductionYear = program.ProductionYear ?? xmltv?.Year,
                StartDate = program.StartDate,
                EndDate = program.EndDate
            };

            _currentPrograms[channelId] = entry;
            return entry;
        }

        /// <summary>
        /// Looks at all active sessions once a minute. This also counts live TV on clients that rarely or never
        /// report playback progress (only start and stop).
        /// </summary>
        private void PollSessions(DateTime now)
        {
            if (!Config.EnableLiveTvTracking)
            {
                ActiveLiveTvSessions = 0;
                return;
            }

            var active = 0;
            foreach (var session in _sessionManager.Sessions.ToList())
            {
                var item = session.FullNowPlayingItem;
                var channelId = GetChannelId(item);
                if (channelId == null && session.NowPlayingItem != null)
                {
                    channelId = session.NowPlayingItem.Type == BaseItemKind.TvChannel
                        ? session.NowPlayingItem.Id
                        : session.NowPlayingItem.Type == BaseItemKind.Program ? session.NowPlayingItem.ChannelId : null;
                }

                if (session.NowPlayingItem != null)
                {
                    LiveTvDiagnostics.Record(
                        new LiveTvDiagnostics.PlaybackSeen
                        {
                            Time = now,
                            Source = "Abfrage",
                            Client = ClientName(session, null, null),
                            UserName = session.UserName,
                            ItemType = item?.GetType().Name ?? session.NowPlayingItem.Type.ToString(),
                            ItemName = item?.Name ?? session.NowPlayingItem.Name,
                            IsLiveTv = channelId != null
                        },
                        false);
                }

                if (channelId == null || session.UserId == Guid.Empty)
                {
                    continue;
                }

                active++;
                var paused = session.PlayState?.IsPaused ?? false;
                var userIds = new[] { session.UserId }.Concat(session.AdditionalUsers.Select(u => u.UserId)).Distinct();
                foreach (var userId in userIds)
                {
                    _tracker.OnProgress(session.Id + "|" + userId.ToString("N"), userId, channelId, paused, now);
                }
            }

            ActiveLiveTvSessions = active;
        }

        private void DoMaintenance()
        {
            try
            {
                var now = DateTime.UtcNow;
                _xmltv.EnsureFresh();
                PollSessions(now);
                _tracker.RemoveStaleSessions(now);

                // Auswertung: live geschaute Sendungen, die vorher empfohlen wurden
                foreach (var entry in _store.GetRecentEntries(now.AddHours(-1), Math.Max(1, Config.LiveTvMinMinutes)))
                {
                    _feedback.RecordWatched(entry.UserId, FeedbackStore.LiveTv, new[] { entry.BroadcastKey, "title:" + LiveTvText.TitleKey(entry) }, entry.LastWatched);
                }

                if (now - _lastPrune > TimeSpan.FromHours(24))
                {
                    var keepDays = Math.Max(1, Config.LiveTvHistoryDays);
                    _store.Prune(now.AddDays(-keepDays));
                    _feedback.Prune(now.AddDays(-Math.Max(365, keepDays)));
                    _lastPrune = now;
                }

                if (now - _lastSave >= SaveInterval)
                {
                    _store.SaveIfDirty();
                    _feedback.SaveIfDirty();
                    _lastSave = now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wartung des Live-TV-Verlaufs fehlgeschlagen");
            }
        }
    }
}
