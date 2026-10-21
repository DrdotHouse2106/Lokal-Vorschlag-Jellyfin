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
            serviceCollection.AddSingleton<CollectionService>();
            serviceCollection.AddSingleton(sp =>
                new Worlds.WorldListSource(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Worlds.WorldListSource>>(),
                    Path.Combine(GetPluginDataPath(sp), "welten-cache.json"),
                    sp.GetService<System.Net.Http.IHttpClientFactory>()));
            serviceCollection.AddSingleton(sp =>
                new Worlds.WorldLogoService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Worlds.WorldLogoService>>(),
                    Path.Combine(GetPluginDataPath(sp), "welten-logos"),
                    sp.GetService<System.Net.Http.IHttpClientFactory>()));
            serviceCollection.AddSingleton<Worlds.WorldService>();

            // Eigener Fernsehprogramm-Anbieter (ersetzt den eingebauten XMLTV-Anbieter)
            serviceCollection.AddSingleton<Epg.EpgListingsProvider>();
            serviceCollection.AddSingleton<MediaBrowser.Controller.LiveTv.IListingsProvider>(sp => sp.GetRequiredService<Epg.EpgListingsProvider>());
            serviceCollection.AddSingleton(sp =>
                new Avatars.AvatarService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Avatars.AvatarService>>(),
                    Path.Combine(GetPluginDataPath(sp), "avatars"),
                    sp.GetService<System.Net.Http.IHttpClientFactory>()));
            serviceCollection.AddSingleton(sp =>
                new Similarity.ExternalSimilarityService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Similarity.ExternalSimilarityService>>(),
                    Path.Combine(GetPluginDataPath(sp), "aehnliche-titel.json")));
            serviceCollection.AddSingleton(sp =>
                new Similarity.TraktPersonalService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Similarity.TraktPersonalService>>(),
                    Path.Combine(GetPluginDataPath(sp), "trakt-konten.json"),
                    Path.Combine(sp.GetRequiredService<MediaBrowser.Controller.IServerApplicationPaths>().PluginConfigurationsPath, "Trakt.xml")));
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

            // Startseite im Quallenkino-Stil für Jellyfin Web (über das Plugin „File Transformation“)
            serviceCollection.AddHostedService<Web.WebIntegration>();

            // Alte Ordner dieses Plugins (auch unter früherem Namen) beim Start entfernen
            serviceCollection.AddHostedService<PluginFolderCleanup>();

            // Empfehlungen für TV-Apps ohne Home Screen Sections (GET /LocalRecs/Recommendations)
            serviceCollection.AddSingleton(sp =>
                new Behavior.BehaviorStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Behavior.BehaviorStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "verhalten.json")));
            serviceCollection.AddSingleton<Behavior.BehaviorService>();
            serviceCollection.AddSingleton<RecommendationSectionHandler>();
            serviceCollection.AddSingleton<LiveTvSectionHandler>();

            // Phase 7b: Live TV (tracking of watched programs and "Heute im TV für dich")
            serviceCollection.AddSingleton(sp =>
                new LiveTvWatchStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveTvWatchStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "livetv-history.json")));
            serviceCollection.AddSingleton<XmltvSource>();
            serviceCollection.AddSingleton<LiveTvRecommendationService>();
            serviceCollection.AddSingleton(sp =>
                new FeedbackStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FeedbackStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "feedback.json")));
            serviceCollection.AddHostedService<LiveTvTrackingService>();

            // Erinnerungen an Live-TV-Sendungen (Kalender-Abo)
            serviceCollection.AddSingleton(sp =>
                new Reminders.ReminderStore(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Reminders.ReminderStore>>(),
                    Path.Combine(GetPluginDataPath(sp), "reminders.json")));
            serviceCollection.AddSingleton<Reminders.ReminderService>();

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
