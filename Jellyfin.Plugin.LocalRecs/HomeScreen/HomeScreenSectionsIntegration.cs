using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.ScheduledTasks;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.HomeScreen
{
    /// <summary>
    /// Meldet die Bereiche „Empfohlene Filme“ und „Empfohlene Serien“ beim Plugin „Home Screen Sections“ an,
    /// räumt Empfehlungs-Bibliotheken älterer Versionen auf und stößt die erste Berechnung an.
    /// </summary>
    public class HomeScreenSectionsIntegration : IHostedService
    {
        /// <summary>
        /// Section ID of the movie recommendations.
        /// </summary>
        public const string MoviesSectionId = "7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a11";

        /// <summary>
        /// Section ID of the series recommendations.
        /// </summary>
        public const string SeriesSectionId = "7d1c2a4e-4b0f-4d7c-9a51-0c6f3e2b8a12";

        private const string HomeScreenSectionsAssemblyName = "Jellyfin.Plugin.HomeScreenSections";
        private const int MaxRegistrationAttempts = 30;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

        private readonly ILogger<HomeScreenSectionsIntegration> _logger;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ITaskManager _taskManager;
        private readonly RecommendationStore _store;
        private readonly LegacyLibraryCleanup _legacyCleanup;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="HomeScreenSectionsIntegration"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="applicationHost">Server application host.</param>
        /// <param name="taskManager">Task manager.</param>
        /// <param name="store">Recommendation store.</param>
        /// <param name="legacyCleanup">Cleanup of libraries created by older versions.</param>
        public HomeScreenSectionsIntegration(
            ILogger<HomeScreenSectionsIntegration> logger,
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

        /// <summary>
        /// Gets a value indicating whether the Home Screen Sections plugin is loaded.
        /// </summary>
        public static bool IsHomeScreenSectionsInstalled => FindRegisterMethod() != null;

        /// <summary>
        /// Gets a value indicating whether the sections have been registered successfully.
        /// </summary>
        public static bool SectionsRegistered { get; private set; }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (Plugin.Instance != null)
            {
                Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
            }

            _ = Task.Run(() => RunAfterStartupAsync(_stoppingCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (Plugin.Instance != null)
            {
                Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
            }

            _stoppingCts.Cancel();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers both sections with Home Screen Sections.
        /// </summary>
        /// <returns>True if the registration succeeded.</returns>
        public bool RegisterSections()
        {
            var registerMethod = FindRegisterMethod();
            if (registerMethod == null)
            {
                return false;
            }

            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

            try
            {
                Register(registerMethod, MoviesSectionId, config.MovieSectionTitle, nameof(RecommendationSectionHandler.GetMovieResults));
                Register(registerMethod, SeriesSectionId, config.TvSectionTitle, nameof(RecommendationSectionHandler.GetSeriesResults));
                SectionsRegistered = true;
                _logger.LogInformation("Startseiten-Bereiche bei „Home Screen Sections“ angemeldet");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Anmeldung der Startseiten-Bereiche bei „Home Screen Sections“ fehlgeschlagen");
                return false;
            }
        }

        private static MethodInfo? FindRegisterMethod()
        {
            var assembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(a => a.GetName().Name == HomeScreenSectionsAssemblyName);

            return assembly?
                .GetType(HomeScreenSectionsAssemblyName + ".PluginInterface")?
                .GetMethod("RegisterSection", BindingFlags.Public | BindingFlags.Static);
        }

        private static void Register(MethodInfo registerMethod, string id, string? title, string resultsMethod)
        {
            var handlerType = typeof(RecommendationSectionHandler);
            var json = JsonSerializer.Serialize(new
            {
                id,
                displayText = string.IsNullOrWhiteSpace(title) ? id : title,
                limit = 1,
                resultsAssembly = handlerType.Assembly.FullName,
                resultsClass = handlerType.FullName,
                resultsMethod
            });

            // RegisterSection erwartet ein Newtonsoft-JObject aus dem Ladekontext von Home Screen Sections
            var payloadType = registerMethod.GetParameters()[0].ParameterType;
            var parseMethod = payloadType.GetMethod("Parse", new[] { typeof(string) })
                ?? throw new InvalidOperationException("JObject.Parse not found");
            var payload = parseMethod.Invoke(null, new object[] { json });

            registerMethod.Invoke(null, new[] { payload });
        }

        private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
        {
            // Geänderte Titel sofort übernehmen
            if (SectionsRegistered)
            {
                RegisterSections();
            }
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

                if (_store.UserCount == 0)
                {
                    _logger.LogInformation("Noch keine Empfehlungen vorhanden – starte die erste Berechnung");
                    _taskManager.QueueIfNotRunning<RecommendationRefreshTask>();
                }

                for (var attempt = 1; attempt <= MaxRegistrationAttempts; attempt++)
                {
                    if (RegisterSections())
                    {
                        return;
                    }

                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogWarning(
                    "Das Plugin „Home Screen Sections“ wurde nicht gefunden. Die Empfehlungen erscheinen erst auf der Startseite, "
                    + "wenn „Home Screen Sections“, „File Transformation“ und „Plugin Pages“ installiert sind.");
            }
            catch (OperationCanceledException)
            {
                // Server wird beendet
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initialisierung der Startseiten-Empfehlungen fehlgeschlagen");
            }
        }
    }
}
