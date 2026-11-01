using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// „Weniger davon“ für „Heute im TV für dich“, je Benutzer: Sendung seltener („weniger“), nie mehr („nie“) oder
    /// ein Genre seltener („genre_weniger“). Wirkt bei der nächsten Abfrage der Empfehlungen.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/LiveTv/Feedback")]
    [Authorize]
    public class LiveTvFeedbackController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly LiveTvFeedbackStore _store;
        private readonly LiveTvRecommendationService _recommendations;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveTvFeedbackController"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="store">„Weniger davon“ store.</param>
        /// <param name="recommendations">Live TV recommendations (cache is dropped after a change).</param>
        public LiveTvFeedbackController(ILibraryManager libraryManager, IUserManager userManager, LiveTvFeedbackStore store, LiveTvRecommendationService recommendations)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _recommendations = recommendations ?? throw new ArgumentNullException(nameof(recommendations));
        }

        /// <summary>
        /// Gets the marks of the user, newest first.
        /// </summary>
        /// <returns>The marks.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<IReadOnlyList<LiveTvFeedbackStore.Mark>> Get()
        {
            var user = RequestUser.Resolve(this, _userManager);
            return user == null ? RequestUser.Missing(this) : Ok(_store.GetMarks(user.Id));
        }

        /// <summary>
        /// Marks a show or genre. The show is named by <c>ProgramId</c> or <c>SeriesId</c> (Jellyfin IDs) or by
        /// <c>Title</c>; for „genre_weniger“ <c>Genre</c> counts, otherwise the first real genre of the program (<see cref="LiveTvDislikes.MainGenre"/>).
        /// </summary>
        /// <param name="request">The mark.</param>
        /// <returns>The stored mark.</returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<LiveTvFeedbackStore.Mark> Post([FromBody] FeedbackRequest request)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            var kind = (request?.Art ?? request?.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != LiveTvDislikes.Less && kind != LiveTvDislikes.GenreLess && kind != LiveTvDislikes.Never)
            {
                return BadRequest("Art muss „weniger“, „genre_weniger“ oder „nie“ sein.");
            }

            var (title, genres) = Describe(request!);
            var now = DateTime.UtcNow;
            if (kind == LiveTvDislikes.GenreLess)
            {
                var genre = string.IsNullOrWhiteSpace(request!.Genre) ? LiveTvDislikes.MainGenre(genres) : request.Genre.Trim();
                if (string.IsNullOrWhiteSpace(genre))
                {
                    return BadRequest("Kein Genre: bitte Genre angeben oder eine Sendung mit Genre.");
                }

                _store.MarkGenre(user.Id, LiveTvText.NormalizeFeature(genre), genre, now);
                _recommendations.Invalidate(user.Id);
                return Ok(new LiveTvFeedbackStore.Mark { Kind = kind, Key = LiveTvText.NormalizeFeature(genre), Genre = genre, At = now });
            }

            var titleKey = LiveTvText.NormalizeTitle(title);
            if (string.IsNullOrEmpty(titleKey))
            {
                return BadRequest("Keine Sendung: bitte ProgramId, SeriesId oder Title angeben.");
            }

            _store.MarkTitle(user.Id, titleKey, title!.Trim(), kind, now);
            _recommendations.Invalidate(user.Id);
            return Ok(new LiveTvFeedbackStore.Mark { Kind = kind, Key = titleKey, Title = title.Trim(), At = now });
        }

        /// <summary>
        /// Takes a mark back („rückgängig“): the show (<c>programId</c>, <c>seriesId</c>, <c>title</c> or <c>key</c> from
        /// the list) or the genre (<c>genre</c>).
        /// </summary>
        /// <param name="programId">Program ID, optional.</param>
        /// <param name="seriesId">Series ID, optional.</param>
        /// <param name="title">Title or normalized key of the show, optional.</param>
        /// <param name="genre">Genre, optional.</param>
        /// <returns>204, or 404 if nothing was marked.</returns>
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult Delete([FromQuery] Guid? programId, [FromQuery] Guid? seriesId, [FromQuery] string? title, [FromQuery] string? genre)
        {
            var user = RequestUser.Resolve(this, _userManager);
            if (user == null)
            {
                return RequestUser.Missing(this);
            }

            bool removed;
            if (!string.IsNullOrWhiteSpace(genre))
            {
                removed = _store.RemoveGenre(user.Id, LiveTvText.NormalizeFeature(genre));
            }
            else
            {
                var (name, _) = Describe(new FeedbackRequest { ProgramId = programId, SeriesId = seriesId, Title = title });
                var titleKey = LiveTvText.NormalizeTitle(name);
                if (string.IsNullOrEmpty(titleKey))
                {
                    return BadRequest("Bitte programId, seriesId, title oder genre angeben.");
                }

                removed = _store.RemoveTitle(user.Id, titleKey);
            }

            if (removed)
            {
                _recommendations.Invalidate(user.Id);
            }

            return removed ? NoContent() : NotFound();
        }

        // Titel (Serienname, sonst Sendungsname) und Genres der genannten Sendung
        private (string? Title, IReadOnlyList<string> Genres) Describe(FeedbackRequest request)
        {
            foreach (var id in new[] { request.ProgramId, request.SeriesId })
            {
                if (id is Guid itemId && itemId != Guid.Empty && _libraryManager.GetItemById(itemId) is BaseItem item)
                {
                    var name = item is LiveTvProgram program && !string.IsNullOrWhiteSpace(program.SeriesName) ? program.SeriesName : item.Name;
                    return (name, item.Genres ?? Array.Empty<string>());
                }
            }

            return (request.Title, Array.Empty<string>());
        }

        /// <summary>A mark request.</summary>
        public class FeedbackRequest
        {
            /// <summary>Gets or sets the Jellyfin ID of the program.</summary>
            public Guid? ProgramId { get; set; }

            /// <summary>Gets or sets the Jellyfin ID of the series (alternative).</summary>
            public Guid? SeriesId { get; set; }

            /// <summary>Gets or sets the title (alternative, e.g. „Tatort“).</summary>
            public string? Title { get; set; }

            /// <summary>Gets or sets the genre for „genre_weniger“, optional.</summary>
            public string? Genre { get; set; }

            /// <summary>Gets or sets the kind: „weniger“, „genre_weniger“ or „nie“.</summary>
            public string? Art { get; set; }

            /// <summary>Gets or sets the kind (English name of <see cref="Art"/>).</summary>
            public string? Kind { get; set; }
        }
    }
}
