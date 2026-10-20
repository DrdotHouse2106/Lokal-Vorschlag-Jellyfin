using System.Net;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Kleine Webseite zum Abonnieren des Kalenders auf dem Handy (Ziel des QR-Codes).
    /// Die iPhone-Kamera öffnet keine webcal-Links direkt – Safari zeigt beim Tippen auf den Knopf den Abo-Dialog.
    /// </summary>
    public static class CalendarPage
    {
        /// <summary>
        /// Builds the page.
        /// </summary>
        /// <param name="webcalUrl">webcal link.</param>
        /// <param name="httpsUrl">https link of the feed.</param>
        /// <param name="calendarName">Name of the calendar.</param>
        /// <returns>The HTML.</returns>
        public static string Build(string webcalUrl, string httpsUrl, string calendarName)
        {
            var webcal = WebUtility.HtmlEncode(webcalUrl);
            var https = WebUtility.HtmlEncode(httpsUrl);
            var name = WebUtility.HtmlEncode(calendarName);
            return "<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\">"
                + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
                + "<meta name=\"robots\" content=\"noindex\"><title>Kalender abonnieren</title><style>"
                + "body{margin:0;padding:24px 16px;background:#0b0e14;color:#fff;font:16px/1.5 -apple-system,system-ui,sans-serif}"
                + "main{max-width:480px;margin:0 auto}h1{font-size:1.5em;margin:.2em 0 .4em}p{color:#c7c7d1}"
                + "a.btn{display:block;margin:1.2em 0;padding:.9em 1em;border-radius:8px;background:#fff;color:#000;font-weight:700;text-align:center;text-decoration:none}"
                + "h2{font-size:1.05em;margin:1.6em 0 .3em}code{display:block;padding:.6em;border-radius:6px;background:#1c2230;color:#e8eaf0;word-break:break-all;font-size:.85em;user-select:all}"
                + "</style></head><body><main>"
                + "<h1>Live-TV-Erinnerungen</h1>"
                + "<p>Kalender „" + name + "“ abonnieren – gemerkte Sendungen erscheinen mit Erinnerung vor Beginn.</p>"
                + "<a class=\"btn\" href=\"" + webcal + "\">Im Kalender abonnieren</a>"
                + "<p>iPhone, iPad und Mac: Knopf antippen und „Abonnieren“ bestätigen.</p>"
                + "<h2>Android / Google Kalender</h2>"
                + "<p>Im Browser auf calendar.google.com unter „Weitere Kalender → Per URL“ diese Adresse einfügen:</p>"
                + "<code id=\"u\">" + https + "</code>"
                + "<a class=\"btn\" href=\"#\" id=\"c\" style=\"background:#3a4152;color:#fff\">Link kopieren</a>"
                + "</main><script>document.getElementById('c').addEventListener('click',function(e){e.preventDefault();var t=document.getElementById('u').textContent,b=this;"
                + "(navigator.clipboard?navigator.clipboard.writeText(t):Promise.reject()).then(function(){b.textContent='Kopiert';},function(){var r=document.createRange();r.selectNodeContents(document.getElementById('u'));var s=getSelection();s.removeAllRanges();s.addRange(r);});});</script>"
                + "</body></html>";
        }
    }
}
