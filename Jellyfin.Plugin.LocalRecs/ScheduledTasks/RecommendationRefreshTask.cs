using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.ScheduledTasks
{
    /// <summary>
    /// Scheduled task for refreshing recommendations for all users.
    /// Can be triggered manually or scheduled via Jellyfin's task scheduler.
    /// </summary>
    public class RecommendationRefreshTask : IScheduledTask
    {
        private readonly ILogger<RecommendationRefreshTask> _logger;
        private readonly IUserManager _userManager;
        private readonly RecommendationRefreshService _refreshService;
        private readonly RecommendationStore _store;
        private readonly FeedbackStore _feedback;
        private readonly ILibraryManager _libraryManager;
        private readonly Collections.MovieSeriesLookup? _movieSeriesLookup;
        private readonly Collections.MovieSeriesService? _movieSeries;
        private readonly MediaBrowser.Controller.Configuration.IServerConfigurationManager? _serverConfig;
        private readonly Seasonal.SeasonalKeywordLookup? _seasonalKeywords;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationRefreshTask"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="refreshService">Recommendation refresh service.</param>
        /// <param name="store">Store that keeps the recommendations for the home screen.</param>
        /// <param name="feedback">Evaluation of the recommendations.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="movieSeriesLookup">Movie series from TMDB, optional.</param>
        /// <param name="movieSeries">Movie series of the users, optional.</param>
        /// <param name="serverConfig">Server configuration (metadata language), optional.</param>
        /// <param name="seasonalKeywords">TMDB keywords for the seasonal row, optional.</param>
        public RecommendationRefreshTask(
            ILogger<RecommendationRefreshTask> logger,
            IUserManager userManager,
            RecommendationRefreshService refreshService,
            RecommendationStore store,
            FeedbackStore feedback,
            ILibraryManager libraryManager,
            Collections.MovieSeriesLookup? movieSeriesLookup = null,
            Collections.MovieSeriesService? movieSeries = null,
            MediaBrowser.Controller.Configuration.IServerConfigurationManager? serverConfig = null,
            Seasonal.SeasonalKeywordLookup? seasonalKeywords = null)
        {
            _seasonalKeywords = seasonalKeywords;
            _movieSeriesLookup = movieSeriesLookup;
            _movieSeries = movieSeries;
            _serverConfig = serverConfig;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        }

        /// <inheritdoc />
        public string Name => "Empfehlungen aktualisieren";

        /// <inheritdoc />
        public string Key => "LocalRecsRefresh";

        /// <inheritdoc />
        public string Description => "Berechnet die persönlichen Empfehlungen aller Benutzer aus Sehverlauf und ähnlichen Titeln neu";

        /// <inheritdoc />
        public string Category => "Quallenkino";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting recommendation refresh task (Build: {BuildVersion})", Plugin.BuildVersion);
            var startTime = DateTime.UtcNow;

            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

                // Step 1: Get all users (5% progress)
                progress?.Report(0);
                cancellationToken.ThrowIfCancellationRequested();
                var users = _userManager.GetUsers().ToList();
                _logger.LogInformation("Generating recommendations for {UserCount} users", users.Count);

                progress?.Report(5);

                if (users.Count == 0)
                {
                    _logger.LogWarning("No users found, skipping recommendation refresh");
                    progress?.Report(100);
                    return;
                }

                // Step 2: Generate recommendations for all users (5-80% progress)
                var userIds = users.Select(u => u.Id).ToList();
                var userRecommendations = await _refreshService.GenerateRecommendationsForMultipleUsersAsync(
                    userIds,
                    config,
                    cancellationToken).ConfigureAwait(false);

                progress?.Report(80);

                // Step 3: Store recommendations for the home screen sections (80-95% progress)
                cancellationToken.ThrowIfCancellationRequested();
                var stored = new Dictionary<Guid, UserRecommendations>();
                var successfulUsers = 0;
                var failedUsers = new List<string>();

                foreach (var user in users)
                {
                    if (userRecommendations.TryGetValue(user.Id, out var recs))
                    {
                        stored[user.Id] = new UserRecommendations
                        {
                            Movies = recs.Movies.Select(r => r.ItemId).ToList(),
                            Series = recs.Tv.Select(r => r.ItemId).ToList(),
                            Reasons = recs.Movies.Concat(recs.Tv)
                                .Where(r => !string.IsNullOrEmpty(r.Reason))
                                .GroupBy(r => r.ItemId)
                                .ToDictionary(g => g.Key, g => g.First().Reason!)
                        };

                        // Welten: Rangliste je Welt, Begründungen ergänzen
                        var worldRankings = _refreshService.GetWorldRankings(user.Id);
                        if (worldRankings != null)
                        {
                            stored[user.Id].Worlds = worldRankings.Items;
                            foreach (var (itemId, reason) in worldRankings.Reasons)
                            {
                                stored[user.Id].Reasons.TryAdd(itemId, reason);
                            }
                        }

                        successfulUsers++;
                        _logger.LogDebug(
                            "Stored recommendations for {UserName}: {MovieCount} movies, {TvCount} TV shows",
                            user.Username,
                            recs.Movies.Count,
                            recs.Tv.Count);
                    }
                    else
                    {
                        failedUsers.Add(user.Username);
                    }
                }

                _store.ReplaceAll(stored);

                // Für die Auswertung merken, was wem empfohlen wurde
                var now = DateTime.UtcNow;
                foreach (var (userId, recs) in stored)
                {
                    _feedback.RecordRecommended(userId, FeedbackStore.Movies, recs.Movies.Select(id => (id.ToString("N"), _libraryManager.GetItemById(id)?.Name ?? string.Empty)), now);
                    _feedback.RecordRecommended(userId, FeedbackStore.Series, recs.Series.Select(id => (id.ToString("N"), _libraryManager.GetItemById(id)?.Name ?? string.Empty)), now);
                }

                _feedback.SaveIfDirty();
                progress?.Report(95);

                // Ähnliche Titel von TMDB/Trakt erst jetzt holen – die Empfehlungen sind schon gespeichert,
                // der nächste Lauf nutzt die neuen Listen
                await _refreshService.RefreshExternalAfterRunAsync(config, cancellationToken).ConfigureAwait(false);

                // Filmreihen: zu welcher TMDB-Sammlung die Filme gehören (nur fehlende bzw. veraltete, nach und nach)
                await RefreshMovieSeriesAsync(config, cancellationToken).ConfigureAwait(false);

                // Jahreszeit: TMDB-Stichwörter der Filme und Serien (nur fehlende bzw. veraltete, nach und nach)
                await RefreshSeasonalAsync(config, cancellationToken).ConfigureAwait(false);

                // Step 4: Report results (100% progress)
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Recommendation refresh completed in {Duration:F2} seconds: {Success}/{Total} users successful",
                    duration.TotalSeconds,
                    successfulUsers,
                    users.Count);

                if (failedUsers.Count > 0)
                {
                    _logger.LogWarning("No recommendations generated for {Count} users: {Users}", failedUsers.Count, string.Join(", ", failedUsers));
                }

                progress?.Report(100);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Recommendation refresh task was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recommendation refresh task failed");
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Daily execution at 4:00 AM by default
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = MediaBrowser.Model.Tasks.TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
                }
            };
        }

        private async Task RefreshSeasonalAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            if (_seasonalKeywords == null || !config.EnableSeasonal)
            {
                return;
            }

            try
            {
                var keys = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true,
                    IsVirtualItem = false
                })
                    .Select(i => (Series: i is MediaBrowser.Controller.Entities.TV.Series, Tmdb: i.GetProviderId(MetadataProvider.Tmdb)))
                    .Where(i => !string.IsNullOrEmpty(i.Tmdb))
                    .Select(i => Seasonal.SeasonalKeywordLookup.Key(i.Series, i.Tmdb!))
                    .ToList();
                await _seasonalKeywords.RefreshAsync(keys, config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Stichwörter zur Jahreszeit konnten nicht abgeglichen werden");
            }
        }

        private async Task RefreshMovieSeriesAsync(PluginConfiguration config, CancellationToken cancellationToken)
        {
            if (_movieSeriesLookup == null || !config.EnableMovieSeriesSection)
            {
                return;
            }

            try
            {
                var ids = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true,
                    IsVirtualItem = false
                })
                    .Where(m => string.IsNullOrEmpty(m.GetProviderId("TmdbCollection")))
                    .Select(m => m.GetProviderId(MetadataProvider.Tmdb))
                    .OfType<string>()
                    .ToList();
                var server = _serverConfig?.Configuration;
                var language = string.IsNullOrEmpty(server?.PreferredMetadataLanguage) ? null
                    : server!.PreferredMetadataLanguage + (string.IsNullOrEmpty(server.MetadataCountryCode) ? string.Empty : "-" + server.MetadataCountryCode);
                if (await _movieSeriesLookup.RefreshAsync(ids, config, language, cancellationToken).ConfigureAwait(false) > 0)
                {
                    _movieSeries?.Invalidate();
                }

                // Teile der Reihen (für „Vor dem Kinostart“), wöchentlich, Kinostart im Land des Servers
                if (_movieSeries != null && config.MomentsCinema)
                {
                    await _movieSeriesLookup.RefreshPartsAsync(_movieSeries.AllSeriesIds(), config, language, server?.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Filmreihen konnten nicht abgeglichen werden");
            }
        }
    }
}
