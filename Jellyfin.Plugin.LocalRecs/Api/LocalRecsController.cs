using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Jellyfin.Plugin.LocalRecs.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// API of the plugin (status for the configuration page and benchmark results).
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    [Authorize(Policy = "RequiresElevation")]
    public class LocalRecsController : ControllerBase
    {
        private readonly ILogger<LocalRecsController> _logger;
        private readonly RecommendationStore _store;
        private readonly LiveTvWatchStore _liveTvWatchStore;
        private readonly FeedbackStore _feedback;
        private readonly MediaBrowser.Controller.Library.IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalRecsController"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="store">Recommendation store.</param>
        /// <param name="liveTvWatchStore">Live TV watch history.</param>
        /// <param name="feedback">Evaluation of the recommendations.</param>
        /// <param name="userManager">User manager.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public LocalRecsController(
            ILogger<LocalRecsController> logger,
            RecommendationStore store,
            LiveTvWatchStore liveTvWatchStore,
            FeedbackStore feedback,
            MediaBrowser.Controller.Library.IUserManager userManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _liveTvWatchStore = liveTvWatchStore ?? throw new ArgumentNullException(nameof(liveTvWatchStore));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        /// <summary>
        /// Gets the status of the home screen integration.
        /// </summary>
        /// <returns>The status.</returns>
        [HttpGet("Status")]
        public ActionResult<StatusInfo> GetStatus()
        {
            return Ok(new StatusInfo
            {
                HomeScreenSectionsInstalled = HomeScreenSectionsIntegration.IsHomeScreenSectionsInstalled,
                SectionsRegistered = HomeScreenSectionsIntegration.SectionsRegistered,
                UsersWithRecommendations = _store.UserCount,
                LastUpdated = _store.LastUpdated,
                LiveTvTrackedBroadcasts = _liveTvWatchStore.Count,
                LiveTvActiveSessions = LiveTvTrackingService.ActiveLiveTvSessions,
                PlaybackEventsReceived = LiveTvDiagnostics.EventCount,
                LiveTvEventsReceived = LiveTvDiagnostics.LiveTvEventCount,
                LastProgramResolved = LiveTvDiagnostics.LastProgramResolved,
                LastProgramMissing = LiveTvDiagnostics.LastProgramMissing,
                RecentPlaybacks = LiveTvDiagnostics.GetRecent(),
                RecommendationsVersion = _store.Version
            });
        }

        /// <summary>
        /// Gets the evaluation of the recommendations: how many recommended titles were watched afterwards.
        /// </summary>
        /// <param name="days">Period in days (default 30).</param>
        /// <returns>The evaluation.</returns>
        [HttpGet("Evaluation")]
        public ActionResult<FeedbackStore.EvaluationReport> GetEvaluation([FromQuery] int days = 30)
        {
            var userNames = _userManager.GetUsers().ToDictionary(u => u.Id, u => u.Username);
            return Ok(_feedback.GetReport(days, DateTime.UtcNow, userNames));
        }

        /// <summary>
        /// Gets the latest benchmark results.
        /// </summary>
        /// <returns>Benchmark results as plain text.</returns>
        [HttpGet("Benchmark/Results")]
        public ActionResult<string> GetBenchmarkResults()
        {
            try
            {
                var pluginDataPath = Plugin.Instance?.DataFolderPath;
                if (string.IsNullOrEmpty(pluginDataPath))
                {
                    return NotFound("Plugin data path not available");
                }

                var resultsPath = Path.Combine(pluginDataPath, "benchmark_results.txt");
                if (!System.IO.File.Exists(resultsPath))
                {
                    return NotFound("No benchmark results found. Run the benchmark task first.");
                }

                var results = System.IO.File.ReadAllText(resultsPath);
                return Ok(new { results, lastModified = System.IO.File.GetLastWriteTimeUtc(resultsPath) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get benchmark results");
                return StatusCode(500, "Failed to retrieve benchmark results");
            }
        }

        /// <summary>
        /// Downloads the benchmark results as a text file.
        /// </summary>
        /// <returns>Benchmark results file.</returns>
        [HttpGet("Benchmark/Download")]
        public ActionResult DownloadBenchmarkResults()
        {
            try
            {
                var pluginDataPath = Plugin.Instance?.DataFolderPath;
                if (string.IsNullOrEmpty(pluginDataPath))
                {
                    return NotFound("Plugin data path not available");
                }

                var resultsPath = Path.Combine(pluginDataPath, "benchmark_results.txt");
                if (!System.IO.File.Exists(resultsPath))
                {
                    return NotFound("No benchmark results found. Run the benchmark task first.");
                }

                var fileBytes = System.IO.File.ReadAllBytes(resultsPath);
                var fileName = $"benchmark_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
                return File(fileBytes, "text/plain", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download benchmark results");
                return StatusCode(500, "Failed to download benchmark results");
            }
        }
    }
}
