using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Behavior
{
    /// <summary>
    /// Bestätigt eine Suche, wenn ein gefundener Titel abgespielt wird (manche Apps spielen direkt aus der Suche ab, ohne
    /// die Detailseite zu öffnen). Nur abgeschlossene Suchen zählen als Interesse.
    /// </summary>
    public class SearchConfirmationService : IHostedService
    {
        private readonly ISessionManager _sessionManager;
        private readonly BehaviorStore _store;
        private readonly ILogger<SearchConfirmationService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchConfirmationService"/> class.
        /// </summary>
        /// <param name="sessionManager">Session manager.</param>
        /// <param name="store">Searches and viewed titles.</param>
        /// <param name="logger">Logger.</param>
        public SearchConfirmationService(ISessionManager sessionManager, BehaviorStore store, ILogger<SearchConfirmationService> logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            return Task.CompletedTask;
        }

        private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (!Config.EnableBehaviorSignals || e.Item == null || e.Users == null)
                {
                    return;
                }

                var ids = e.Item is Episode episode && episode.SeriesId != Guid.Empty ? new[] { e.Item.Id, episode.SeriesId } : new[] { e.Item.Id };
                foreach (var user in e.Users)
                {
                    if (_store.IsEnabled(user.Id))
                    {
                        _store.ConfirmSearch(user.Id, ids, DateTime.UtcNow);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Suche konnte nicht bestätigt werden");
            }
        }
    }
}
