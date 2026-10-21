using System;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Similarity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Admin: Stand der ähnlichen Titel von TMDB/Trakt – vor allem, welcher TMDB-Schlüssel genutzt wird.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Similarity")]
    [Authorize(Policy = "RequiresElevation")]
    public class SimilarityController : ControllerBase
    {
        private readonly ExternalSimilarityService _external;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimilarityController"/> class.
        /// </summary>
        /// <param name="external">Similarity service.</param>
        public SimilarityController(ExternalSimilarityService external)
        {
            _external = external ?? throw new ArgumentNullException(nameof(external));
        }

        /// <summary>
        /// Gets the state: source of the TMDB key (never the key itself), whether Trakt is set up, size of the cache.
        /// </summary>
        /// <returns>The state.</returns>
        [HttpGet("Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<SimilarityStatus> GetStatus()
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var (_, source) = ExternalSimilarityService.ResolveTmdbKeyWithSource(config);
            var (count, newest) = _external.CacheInfo();
            return new SimilarityStatus
            {
                TmdbKeySource = source,
                TraktRelated = !string.IsNullOrWhiteSpace(config.TraktClientId),
                CachedTitles = count,
                NewestFetch = newest
            };
        }

        /// <summary>
        /// State of the similar titles.
        /// </summary>
        public class SimilarityStatus
        {
            /// <summary>Gets or sets the source of the TMDB key: "eigen", "jellyfin-einstellung", "jellyfin-eingebaut" or null (none).</summary>
            public string? TmdbKeySource { get; set; }

            /// <summary>Gets or sets a value indicating whether Trakt "related" lists are fetched (own Trakt app).</summary>
            public bool TraktRelated { get; set; }

            /// <summary>Gets or sets the number of cached lists.</summary>
            public int CachedTitles { get; set; }

            /// <summary>Gets or sets the newest fetch time (UTC).</summary>
            public DateTime? NewestFetch { get; set; }
        }
    }
}
