using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.ScheduledTasks;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Nach dem Serverstart: alte Bibliotheken früherer Versionen aufräumen und Empfehlungen berechnen, wenn noch keine da sind
    /// oder sie älter als 24 Stunden sind. Die Startseite zeichnet das Web-Modul selbst (seit 12.48.0 ohne „Home Screen Sections“).
    /// </summary>
    public class StartupService : IHostedService
    {
        private readonly ILogger<StartupService> _logger;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ITaskManager _taskManager;
        private readonly RecommendationStore _store;
        private readonly LegacyLibraryCleanup _legacyCleanup;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="StartupService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="applicationHost">Server application host.</param>
        /// <param name="taskManager">Task manager.</param>
        /// <param name="store">Recommendation store.</param>
        /// <param name="legacyCleanup">Cleanup of libraries created by older versions.</param>
        public StartupService(
            ILogger<StartupService> logger,
            IServerApplicationHost applicationHost,
            ITaskManager taskManager,
            RecommendationStore store,
            LegacyLibraryCleanup legacyCleanup)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _applicationHost = applicationHost ?? throw new ArgumentNullException(nameof(applicationHost));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _legacyCleanup = legacyCleanup ?? throw new ArgumentNullException(nameof(legacyCleanup));
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(() => RunAfterStartupAsync(_stoppingCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _stoppingCts.Cancel();
            return Task.CompletedTask;
        }

        private async Task RunAfterStartupAsync(CancellationToken cancellationToken)
        {
            try
            {
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (!_applicationHost.CoreStartupHasCompleted && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }

                await _legacyCleanup.RunAsync(cancellationToken).ConfigureAwait(false);

                // Neu berechnen, wenn noch nichts da ist oder die Daten veraltet sind. Ein Update allein löst keine
                // Neuberechnung beim Start aus; die gespeicherten Empfehlungen bleiben gültig.
                if (_store.UserCount == 0
                    || _store.LastUpdated == null
                    || DateTime.UtcNow - _store.LastUpdated.Value > TimeSpan.FromHours(24))
                {
                    _logger.LogInformation(
                        "Empfehlungen werden neu berechnet (gespeichert: Version {OldVersion} vom {LastUpdated}, aktuell: {Version})",
                        _store.Version ?? "-",
                        _store.LastUpdated,
                        RecommendationStore.CurrentVersion);
                    _taskManager.QueueIfNotRunning<RecommendationRefreshTask>();
                }
            }
            catch (OperationCanceledException)
            {
                // Server wird beendet
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Start der Empfehlungen fehlgeschlagen");
            }
        }
    }
}
