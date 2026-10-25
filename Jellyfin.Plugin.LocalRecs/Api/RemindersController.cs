using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Reminders;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Live-TV-Sendungen merken und als Kalender abonnieren (iCal mit Erinnerung vor Beginn).
    /// Program-IDs werden mit und ohne Bindestriche angenommen.
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    public class RemindersController : ControllerBase
    {
        // Kalender-Apps fragen oft ab: den Feed je Benutzer 60 Sekunden zwischenspeichern (höchstens 500 Benutzer)
        private static readonly TimeSpan FeedCacheDuration = TimeSpan.FromSeconds(60);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (DateTime Created, byte[] Ics)> FeedCache = new();

        private readonly ReminderService _reminders;
        private readonly IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemindersController"/> class.
        /// </summary>
        /// <param name="reminders">Reminder service.</param>
        /// <param name="userManager">User manager.</param>
        public RemindersController(ReminderService reminders, IUserManager userManager)
        {
            _reminders = reminders ?? throw new ArgumentNullException(nameof(reminders));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the reminders of the signed-in user: remembered programmes and upcoming broadcasts of remembered series.
        /// </summary>
        /// <returns>The programmes by start.</returns>
        [HttpGet("Reminders")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<ReminderEntry>> GetReminders()
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            return Ok(_reminders.GetEntries(user, DateTime.UtcNow));
        }

        /// <summary>
        /// Gets the remembered series of the signed-in user.
        /// </summary>
        /// <returns>The series.</returns>
        [HttpGet("Reminders/Series")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<SeriesReminder>> GetSeries()
        {
            var user = CurrentUser();
            return user == null ? Unauthorized() : Ok(_reminders.Store.GetSeries(user.Id));
        }

        /// <summary>
        /// Remembers a programme. 204 = remembered (also if already), 404 = unknown programme, 409 = already over,
        /// 403 = no live TV access.
        /// </summary>
        /// <param name="programId">Programme ID.</param>
        /// <returns>No content.</returns>
        [HttpPost("Reminders/{programId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public ActionResult AddReminder([FromRoute] string programId)
        {
            var user = CurrentUser();
            ForgetFeed(user);
            if (user == null)
            {
                return Unauthorized();
            }

            if (!user.HasPermission(PermissionKind.EnableLiveTvAccess))
            {
                return Forbid();
            }

            var program = Guid.TryParse(programId, out var id) ? _reminders.GetProgram(id) : null;
            if (program == null)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;
            var entry = _reminders.Snapshot(program);
            if (entry.EndDate <= now)
            {
                return Conflict("Die Sendung ist schon vorbei.");
            }

            _reminders.Store.AddProgram(user.Id, entry, now);
            return NoContent();
        }

        /// <summary>
        /// Forgets a programme. 204 = removed, 404 = was not remembered.
        /// </summary>
        /// <param name="programId">Programme ID.</param>
        /// <returns>No content.</returns>
        [HttpDelete("Reminders/{programId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult RemoveReminder([FromRoute] string programId)
        {
            var user = CurrentUser();
            ForgetFeed(user);
            if (user == null)
            {
                return Unauthorized();
            }

            return Guid.TryParse(programId, out var id) && _reminders.Store.RemoveProgram(user.Id, id.ToString("N")) ? NoContent() : NotFound();
        }

        /// <summary>
        /// Remembers the series of a programme (all future broadcasts with the same title). 200 with the series,
        /// 404 = unknown programme.
        /// </summary>
        /// <param name="programId">Programme ID.</param>
        /// <returns>The remembered series.</returns>
        [HttpPost("Reminders/Series/{programId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<SeriesReminder> AddSeries([FromRoute] string programId)
        {
            var user = CurrentUser();
            ForgetFeed(user);
            if (user == null)
            {
                return Unauthorized();
            }

            if (!user.HasPermission(PermissionKind.EnableLiveTvAccess))
            {
                return Forbid();
            }

            var program = Guid.TryParse(programId, out var id) ? _reminders.GetProgram(id) : null;
            if (program == null)
            {
                return NotFound();
            }

            var key = ReminderService.SeriesKey(program.SeriesName, program.Name);
            var name = string.IsNullOrWhiteSpace(program.SeriesName) ? program.Name ?? key : program.SeriesName;
            _reminders.Store.AddSeries(user.Id, key, name, DateTime.UtcNow);
            return Ok(new SeriesReminder { Key = key, Name = name, Added = DateTime.UtcNow });
        }

        /// <summary>
        /// Forgets a series – by its key (from GET Reminders/Series) or by a programme of the series.
        /// 204 = removed, 404 = was not remembered.
        /// </summary>
        /// <param name="key">Series key or programme ID.</param>
        /// <returns>No content.</returns>
        [HttpDelete("Reminders/Series/{key}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult RemoveSeries([FromRoute] string key)
        {
            var user = CurrentUser();
            ForgetFeed(user);
            if (user == null)
            {
                return Unauthorized();
            }

            if (_reminders.Store.RemoveSeries(user.Id, key))
            {
                return NoContent();
            }

            var program = Guid.TryParse(key, out var id) ? _reminders.GetProgram(id) : null;
            return program != null && _reminders.Store.RemoveSeries(user.Id, ReminderService.SeriesKey(program.SeriesName, program.Name)) ? NoContent() : NotFound();
        }

        /// <summary>
        /// Gets the calendar subscription of the signed-in user (created on first call).
        /// </summary>
        /// <returns>Links: https, webcal and QR code (SVG).</returns>
        [HttpGet("Calendar")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<CalendarLink> GetCalendar()
        {
            var user = CurrentUser();
            return user == null ? Unauthorized() : Ok(Link(_reminders.Store.GetToken(user.Id, false)));
        }

        /// <summary>
        /// Creates a new calendar link; the old one stops working.
        /// </summary>
        /// <returns>The new links.</returns>
        [HttpPost("Calendar/Renew")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<CalendarLink> RenewCalendar()
        {
            var user = CurrentUser();
            ForgetFeed(user);
            return user == null ? Unauthorized() : Ok(Link(_reminders.Store.GetToken(user.Id, true)));
        }

        /// <summary>
        /// Gets a QR code (SVG, scalable, with quiet zone) of the calendar link, e.g. to scan from the TV screen.
        /// </summary>
        /// <param name="target">"page" (default: web page with subscribe button – the iPhone camera does not open webcal links), "webcal" or "https".</param>
        /// <returns>The SVG image.</returns>
        [HttpGet("Calendar/Qr")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetCalendarQr([FromQuery] string target = "page")
        {
            var user = CurrentUser();
            if (user == null)
            {
                return Unauthorized();
            }

            var link = Link(_reminders.Store.GetToken(user.Id, false));
            var text = string.Equals(target, "https", StringComparison.OrdinalIgnoreCase) ? link.Url
                : string.Equals(target, "webcal", StringComparison.OrdinalIgnoreCase) ? link.WebcalUrl
                : link.PageUrl;
            Response.Headers.CacheControl = "no-store";
            return Content(QrCode.Encode(text).ToSvg(), "image/svg+xml");
        }

        /// <summary>
        /// The calendar feed (iCal). Without sign-in – calendar apps cannot sign in; the secret token in the link protects it.
        /// </summary>
        /// <param name="token">Secret token.</param>
        /// <returns>The calendar.</returns>
        [HttpGet("Calendar/{token}.ics")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult GetCalendarFeed([FromRoute] string token)
        {
            var userId = _reminders.Store.FindUserByToken(token);
            var user = userId.HasValue ? _userManager.GetUserById(userId.Value) : null;
            if (user == null)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;
            Response.Headers.CacheControl = "no-cache";
            if (FeedCache.TryGetValue(user.Id, out var cached) && now - cached.Created < FeedCacheDuration)
            {
                return File(cached.Ics, "text/calendar; charset=utf-8");
            }

            var ics = Encoding.UTF8.GetBytes(IcsBuilder.Build(_reminders.GetEntries(user, now), "Live-TV – " + user.Username, Math.Max(0, Config.CalendarAlarmMinutes), now));
            if (FeedCache.Count > 500)
            {
                FeedCache.Clear();
            }

            FeedCache[user.Id] = (now, ics);
            return File(ics, "text/calendar; charset=utf-8");
        }

        /// <summary>
        /// Web page for subscribing on a phone (target of the QR code): button for iPhone/Mac (webcal) and the link for Google Calendar.
        /// Without sign-in, protected by the secret token like the feed.
        /// </summary>
        /// <param name="token">Secret token.</param>
        /// <returns>The HTML page.</returns>
        [HttpGet("Calendar/{token}/Abonnieren")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult GetCalendarPage([FromRoute] string token)
        {
            var userId = _reminders.Store.FindUserByToken(token);
            var user = userId.HasValue ? _userManager.GetUserById(userId.Value) : null;
            if (user == null)
            {
                return NotFound();
            }

            var link = Link(token);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            return Content(CalendarPage.Build(link.WebcalUrl, link.Url, "Live-TV – " + user.Username), "text/html; charset=utf-8");
        }

        // Nach Änderungen sieht der nächste Kalender-Abruf sofort den neuen Stand
        private static void ForgetFeed(Jellyfin.Database.Implementations.Entities.User? user)
        {
            if (user != null)
            {
                FeedCache.TryRemove(user.Id, out _);
            }
        }

        private Jellyfin.Database.Implementations.Entities.User? CurrentUser() => RequestUser.Resolve(this, _userManager);

        private string PublicBase()
        {
            var configured = Config.CalendarPublicUrl?.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            // Keine X-Forwarded-*-Header selbst auswerten (beliebig fälschbar): Hinter einem Proxy setzt Jellyfin Scheme und
            // Host schon selbst, wenn der Proxy unter „Bekannte Proxys“ eingetragen ist – sonst „Öffentliche Adresse“ setzen.
            return Request.Scheme + "://" + Request.Host.Value + Request.PathBase;
        }

        private CalendarLink Link(string token)
        {
            var url = PublicBase() + "/LocalRecs/Calendar/" + token + ".ics";
            var webcal = "webcal://" + url.Substring(url.IndexOf("://", StringComparison.Ordinal) + 3);
            return new CalendarLink { Url = url, WebcalUrl = webcal, PageUrl = PublicBase() + "/LocalRecs/Calendar/" + token + "/Abonnieren", QrUrl = "/LocalRecs/Calendar/Qr" };
        }

        /// <summary>
        /// Calendar subscription links.
        /// </summary>
        public class CalendarLink
        {
            /// <summary>Gets or sets the https link (e.g. for Google Calendar „Per URL hinzufügen“).</summary>
            public string Url { get; set; } = string.Empty;

            /// <summary>Gets or sets the webcal link (subscribes directly on iPhone/Mac/Outlook).</summary>
            public string WebcalUrl { get; set; } = string.Empty;

            /// <summary>Gets or sets the web page with subscribe button (target of the QR code, since 12.23.0).</summary>
            public string PageUrl { get; set; } = string.Empty;

            /// <summary>Gets or sets the path of the QR code (SVG) on this server (page; ?target=webcal or ?target=https for the links).</summary>
            public string QrUrl { get; set; } = string.Empty;
        }
    }
}
