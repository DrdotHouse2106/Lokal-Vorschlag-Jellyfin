using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
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
        public LiveTvTrackingService(
            ILogger<LiveTvTrackingService> logger,
            ISessionManager sessionManager,
            ILibraryManager libraryManager,
            LiveTvWatchStore store)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tracker = new LiveTvWatchTracker(store, ResolveProgram);
        }

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

        private static string SessionKey(PlaybackProgressEventArgs e, Guid userId)
        {
            var session = e.Session?.Id ?? e.DeviceId ?? e.PlaySessionId ?? string.Empty;
            return session + "|" + userId.ToString("N");
        }

        private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (!Config.EnableLiveTvTracking || e.Users == null)
                {
                    return;
                }

                var channelId = GetChannelId(e.Item);
                var now = DateTime.UtcNow;
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
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ende der Live-TV-Wiedergabe konnte nicht erfasst werden");
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

            if (program == null)
            {
                return null;
            }

            var entry = new LiveTvWatchEntry
            {
                BroadcastKey = channelId.ToString("N") + "|" + program.StartDate.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
                Title = program.Name ?? string.Empty,
                SeriesName = string.IsNullOrWhiteSpace(program.SeriesName) ? null : program.SeriesName,
                EpisodeTitle = program.EpisodeTitle,
                Genres = program.Genres?.ToList() ?? new System.Collections.Generic.List<string>(),
                People = _libraryManager.GetPeople(program).Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(10).ToList()!,
                ChannelName = _libraryManager.GetItemById(channelId)?.Name,
                IsMovie = program.IsMovie,
                IsSports = program.IsSports,
                ProductionYear = program.ProductionYear,
                StartDate = program.StartDate,
                EndDate = program.EndDate
            };

            _currentPrograms[channelId] = entry;
            return entry;
        }

        private void DoMaintenance()
        {
            try
            {
                var now = DateTime.UtcNow;
                _tracker.RemoveStaleSessions(now);

                if (now - _lastPrune > TimeSpan.FromHours(24))
                {
                    _store.Prune(now.AddDays(-Math.Max(1, Config.LiveTvHistoryDays)));
                    _lastPrune = now;
                }

                if (now - _lastSave >= SaveInterval)
                {
                    _store.SaveIfDirty();
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
