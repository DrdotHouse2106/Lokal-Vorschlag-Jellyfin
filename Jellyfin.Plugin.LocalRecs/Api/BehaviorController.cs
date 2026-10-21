using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Behavior;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Verhalten des angemeldeten Benutzers: Suchen und angesehene Titel melden (kurzfristiges Interesse), eigenen Stand
    /// ansehen, abschalten oder löschen, und die Reihenfolge der Empfehlungszeilen für die Tageszeit abfragen.
    /// Jeder nur für sich selbst.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Behavior")]
    [Authorize]
    public class BehaviorController : ControllerBase
    {
        private readonly BehaviorStore _store;
        private readonly BehaviorService _behavior;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="BehaviorController"/> class.
        /// </summary>
        /// <param name="store">Searches and viewed titles.</param>
        /// <param name="behavior">Behavior service.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        public BehaviorController(BehaviorStore store, BehaviorService behavior, IUserManager userManager, ILibraryManager libraryManager)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Notes a search of the signed-in user (only if switched on for the server and the user).
        /// </summary>
        /// <param name="request">Search term (2–80 characters).</param>
        /// <returns>No content.</returns>
        [HttpPost("Search")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Search([FromBody] SearchRequest request)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            var term = request?.Term?.Trim();
            if (!Config.EnableBehaviorSignals || !_store.IsEnabled(user.Id) || term == null || term.Length < 2 || term.Length > 80)
            {
                return NoContent();
            }

            var matches = _behavior.ResolveSearch(user, term);
            if (matches.Count > 0)
            {
                _store.AddSearch(user.Id, term, matches, DateTime.UtcNow);
            }

            return NoContent();
        }

        /// <summary>
        /// Notes that the signed-in user opened the detail page of a title (only if switched on).
        /// </summary>
        /// <param name="request">The title.</param>
        /// <returns>No content.</returns>
        [HttpPost("View")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult View([FromBody] ViewRequest request)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            if (Config.EnableBehaviorSignals && _store.IsEnabled(user.Id) && request != null
                && _libraryManager.GetItemById(request.ItemId) is { } item && item.IsVisibleStandalone(user)
                && (item is MediaBrowser.Controller.Entities.Movies.Movie || item is MediaBrowser.Controller.Entities.TV.Series))
            {
                _store.AddView(user.Id, item.Id, DateTime.UtcNow);
            }

            return NoContent();
        }

        /// <summary>
        /// Gets what is noted about the signed-in user and the time-of-day state.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone, optional.</param>
        /// <returns>The state.</returns>
        [HttpGet("Me")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<BehaviorState> Get([FromQuery] int? utcOffsetMinutes = null)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            Response.Headers.CacheControl = "no-store";
            var stored = _store.Get(user.Id);
            var daypart = _behavior.RowOrder(user, utcOffsetMinutes);
            return new BehaviorState
            {
                FeatureEnabled = Config.EnableBehaviorSignals,
                Enabled = _store.IsEnabled(user.Id),
                RetentionDays = _store.RetentionDays,
                MaxEntries = _store.MaxEntries,
                DaypartEnabled = Config.EnableDaypart,
                Daypart = daypart.Label,
                PlaysInDaypart = daypart.Plays,
                Order = daypart.Order,
                Searches = stored.Searches.AsEnumerable().Reverse().Select(s => new NotedSearch { Term = s.Term, At = s.At }).ToList(),
                Views = stored.Views.AsEnumerable().Reverse()
                    .Select(v => new NotedView { ItemId = v.ItemId.ToString("N"), Name = _libraryManager.GetItemById(v.ItemId)?.Name ?? string.Empty, At = v.At })
                    .ToList()
            };
        }

        /// <summary>
        /// Switches noting searches and viewed titles on or off for the signed-in user (off deletes the entries).
        /// </summary>
        /// <param name="request">On or off.</param>
        /// <returns>No content.</returns>
        [HttpPost("Me")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Set([FromBody] EnabledRequest request)
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty || request == null)
            {
                return Unauthorized();
            }

            _store.SetEnabled(userId, request.Enabled);
            _behavior.Forget(userId);
            return NoContent();
        }

        /// <summary>
        /// Deletes the noted searches and viewed titles of the signed-in user.
        /// </summary>
        /// <returns>No content.</returns>
        [HttpDelete("Me")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Clear()
        {
            var userId = RecommendationsController.GetUserId(User);
            if (userId == Guid.Empty)
            {
                return Unauthorized();
            }

            _store.Clear(userId);
            _behavior.Forget(userId);
            return NoContent();
        }

        /// <summary>
        /// Gets the order of the recommendation rows for now (movies, series, live TV), most typical for the time of day first.
        /// </summary>
        /// <param name="utcOffsetMinutes">Offset of the user's time zone in minutes (e.g. 120 for CEST), optional.</param>
        /// <returns>Daypart and order.</returns>
        [HttpGet("Daypart")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<DaypartState> GetDaypart([FromQuery] int? utcOffsetMinutes = null)
        {
            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            var (_, label, order, plays) = _behavior.RowOrder(user, utcOffsetMinutes);
            return new DaypartState { Daypart = label, Order = order, Plays = plays };
        }

        /// <summary>A search.</summary>
        public class SearchRequest
        {
            /// <summary>Gets or sets the search term.</summary>
            public string? Term { get; set; }
        }

        /// <summary>A viewed title.</summary>
        public class ViewRequest
        {
            /// <summary>Gets or sets the title.</summary>
            public Guid ItemId { get; set; }
        }

        /// <summary>On or off.</summary>
        public class EnabledRequest
        {
            /// <summary>Gets or sets a value indicating whether noting is on.</summary>
            public bool Enabled { get; set; }
        }

        /// <summary>Order of the rows for the time of day.</summary>
        public class DaypartState
        {
            /// <summary>Gets or sets the daypart ("morgens", "tagsüber", "abends", "nachts").</summary>
            public string Daypart { get; set; } = string.Empty;

            /// <summary>Gets or sets the kinds ("movies", "series", "livetv"), most typical first.</summary>
            public IReadOnlyList<string> Order { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets the number of plays in this daypart (the order changes from 15).</summary>
            public int Plays { get; set; }
        }

        /// <summary>What is noted about a user.</summary>
        public class BehaviorState : DaypartState
        {
            /// <summary>Gets or sets a value indicating whether the admin switched noting on.</summary>
            public bool FeatureEnabled { get; set; }

            /// <summary>Gets or sets a value indicating whether the user lets the plugin note searches and viewed titles.</summary>
            public bool Enabled { get; set; }

            /// <summary>Gets or sets how many days entries are kept.</summary>
            public int RetentionDays { get; set; }

            /// <summary>Gets or sets how many searches and how many viewed titles are kept.</summary>
            public int MaxEntries { get; set; }

            /// <summary>Gets or sets a value indicating whether the time of day counts.</summary>
            public bool DaypartEnabled { get; set; }

            /// <summary>Gets or sets the number of plays in the current daypart.</summary>
            public int PlaysInDaypart { get; set; }

            /// <summary>Gets or sets the noted searches, newest first.</summary>
            public IReadOnlyList<NotedSearch> Searches { get; set; } = Array.Empty<NotedSearch>();

            /// <summary>Gets or sets the noted viewed titles, newest first.</summary>
            public IReadOnlyList<NotedView> Views { get; set; } = Array.Empty<NotedView>();
        }

        /// <summary>A noted search.</summary>
        public class NotedSearch
        {
            /// <summary>Gets or sets the search term.</summary>
            public string Term { get; set; } = string.Empty;

            /// <summary>Gets or sets when (UTC).</summary>
            public DateTime At { get; set; }
        }

        /// <summary>A noted viewed title.</summary>
        public class NotedView
        {
            /// <summary>Gets or sets the item ID.</summary>
            public string ItemId { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets when (UTC).</summary>
            public DateTime At { get; set; }
        }
    }
}
