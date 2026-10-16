using System;
using System.IO;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Jellyfin.Plugin.LocalRecs.ScheduledTasks;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LocalRecs
{
    /// <summary>
    /// Register services for dependency injection.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Phase 3: Embedding Layer Services
            serviceCollection.AddSingleton<LibraryAnalysisService>();
            serviceCollection.AddSingleton<VocabularyBuilder>();
            serviceCollection.AddSingleton<EmbeddingService>();

            // Phase 4: User Profile Service
            serviceCollection.AddSingleton<UserProfileService>();

            // Phase 5: Recommendation Engine
            serviceCollection.AddSingleton<RecommendationEngine>();

            // Phase 6: Recommendation Refresh Service
            serviceCollection.AddSingleton<RecommendationRefreshService>();

            // Phase 7: Home screen sections (via the "Home Screen Sections" plugin)
            serviceCollection.AddSingleton(sp =>
                new RecommendationStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecommendationStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "recommendations.json")));

            serviceCollection.AddSingleton(sp =>
                new LegacyLibraryCleanup(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LegacyLibraryCleanup>>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.IUserManager>(),
                    Path.Combine(GetPluginDataPath(sp), "virtual-libraries")));

            serviceCollection.AddHostedService<HomeScreenSectionsIntegration>();

            // Phase 7b: Live TV (tracking of watched programs and "Heute im TV für dich")
            serviceCollection.AddSingleton(sp =>
                new LiveTvWatchStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveTvWatchStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "livetv-history.json")));
            serviceCollection.AddSingleton<LiveTvRecommendationService>();
            serviceCollection.AddHostedService<LiveTvTrackingService>();

            // Phase 8: Scheduled Tasks
            serviceCollection.AddTransient<IScheduledTask, RecommendationRefreshTask>();
            serviceCollection.AddTransient<IScheduledTask, BenchmarkTask>();
        }

        private static string GetPluginDataPath(IServiceProvider serviceProvider)
        {
            // Same folder that older versions used for the virtual libraries
            var appPaths = serviceProvider.GetRequiredService<IApplicationPaths>();
            return Path.Combine(appPaths.PluginsPath, "LocalRecs");
        }
    }
}
