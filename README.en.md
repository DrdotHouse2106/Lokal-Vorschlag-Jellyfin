# Jellyfin Local Recommendations Plug-in

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

[![Buy me a coffee](https://img.shields.io/badge/☕_Buy_me_a_coffee-PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/DrdotHouse)

Privacy-first personalized recommendations for Jellyfin based entirely on local watch history and metadata similarity. No cloud services, no tracking. Recommendations appear as rows on the home screen.

> [!NOTE]
> **Based on / original:** This plugin is based on the original project **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** by Roger Pharr.
> This repository is a version adapted for **Jellyfin 12**, as the original will not be updated for Jellyfin 12. All credit for the actual development goes to the original author.
>
> German version: [README.md](README.md)

Please report any issues or feedback on [GitHub Issues](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/issues).

> [!IMPORTANT]
> Since **12.2.0** recommendations are shown as **rows on the home screen** (via the "Home Screen Sections" plugin) instead of per-user libraries. Recommendation libraries created by older versions are removed automatically on first start. **Native apps** (Android TV, Fire TV, Roku, Swiftfin on iPhone/iPad/Apple TV) do not show these rows. For Android TV / Fire TV there is the customized app [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client), which loads the recommendations via `GET /LocalRecs/Recommendations` (since 12.5.0).

## Features

- **Per-user personalization** - Tailored recommendations for each user's viewing history
- **Content-based filtering** - TF-IDF embeddings with cosine similarity matching
- **Temporal similarity** - Decade-based grouping finds content from similar time periods
- **Home screen rows** - "Empfohlene Filme für dich" / "Empfohlene Serien für dich", individual per user
- **Real library items** - full metadata, shared watch state, no duplicates in "Continue Watching", no extra libraries
- **Live TV** - tracks which programs you watch live (all clients, including TV apps) and shows the row **"Heute im TV für dich"** with matching upcoming programs from the EPG
- **Privacy-first** - All processing happens locally on your server
- **Performance optimized** - Handles 2,000+ item libraries efficiently

## Requirements

- **Jellyfin Server:** 12.0+ (tested on 12.2)
- **.NET Runtime:** 10.0
- **Target ABI:** 12.0.0.0
- **For the home screen rows:** the plugins **Home Screen Sections**, **File Transformation** and **Plugin Pages** by IAmParadox27 (repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`)
- **Clients:** web, desktop app, the Android app "Jellyfin" and the iOS app "Jellyfin Mobile" (both show the web interface). Native apps (Android TV, Fire TV, Roku, **Swiftfin** on iPhone/iPad/Apple TV) do not show the rows.

For Jellyfin 10.11.x use upstream version 0.6.1.

## Installation

1. **Add plugin repository:**  
   Dashboard → Plugins → Repositories → Add  
   `https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Plugins/main/manifest.json`  
   (the shared plugin repository [DrdotHouse2106/Jellyfin-Plugins](https://github.com/DrdotHouse2106/Jellyfin-Plugins); the old URL `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json` keeps working)

2. **Install plugin:**  
   Dashboard → Plugins → Catalog → Install "Local Recommendations"

3. **Restart Jellyfin server**

4. **Set up the home screen rows** (see Setup below)

## Setup

1. Add the repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` and install **Home Screen Sections**, **File Transformation** and **Plugin Pages**. Restart Jellyfin.
2. Dashboard → Plugins → **Home Screen Sections**: enable the plugin and enable the sections **"Empfohlene Filme für dich"** and **"Empfohlene Serien für dich"** for all users (or let every user enable them via "Modular Home").
3. Done. The first calculation starts automatically after server start; afterwards the scheduled task "Refresh Local Recommendations" runs daily at 4:00 AM.

The plugin's **Startseite** tab shows the connection status and lets you change the row titles. Watched or started movies disappear from the row immediately. Every user only sees their own recommendations.

## Configuration

Access via: **Dashboard → Plugins → Local Recommendations → Settings**

### Key Settings

**Recommendation Counts**
- Movies and TV shows to recommend per user (default: 25 each)
- Minimum watched items for personalization (default: 3)

**Filtering**
- Exclude abandoned TV series (default: enabled, 90 days threshold)

**Weighting Factors**
- Favorite boost: 2.0x (configurable)
- Recent watch emphasis: 1.0 (configurable) — amplifies recently watched items using decay²
- Recency decay half-life: 365 days (configurable)

**Optional Features**
- Rating proximity scoring (boost items with similar ratings)
- Decade-based temporal similarity (finds content from similar eras)

**Performance**
- Vocabulary limits for actors/tags (default: 500 each) — see note below
- Parallel processing options

**Vocabulary size** controls how many distinct actors, directors, and tags are included in the TF-IDF model. A higher value (e.g. 1000) captures more niche contributors and gives richer signals for large, varied libraries. A lower value (e.g. 200) is faster and uses less memory but may miss less-common cast members. The default of 500 is a good starting point for most libraries; raise it if recommendations feel too genre-driven and ignore specific actors you watch often.

**Recency decay** controls how much your recent watch history influences recommendations relative to older watches. It is expressed as a half-life in days: a value of 365 means a film watched a year ago contributes half as much to your taste profile as one watched today. Lower values (e.g. 90) make recommendations react quickly to recent binges; higher values (e.g. 730) give a more stable, long-term taste profile.

**Update Schedule**
- Default: Daily at 4:00 AM
- Customize in Dashboard → Scheduled Tasks
- Manual refresh available anytime

## How It Works

### Algorithm

**Content-based filtering** using TF-IDF embeddings and cosine similarity:

1. **Feature extraction** - Genres, actors, directors, tags, decades, ratings
2. **TF-IDF embeddings** - Numerical vectors for each item (~1200-1500 dimensions)
3. **User profiles** - Aggregated taste vector from weighted watch history
4. **Similarity scoring** - Cosine similarity between user profile and unwatched items
5. **Ranking** - Top N items sorted by similarity score

**Weighting factors:**
- Favorites (2x boost)
- Recent watch emphasis (decay² amplification, default 1.0)
- Recency decay (365-day half-life)
- Decade similarity (items from similar time periods)
- Optional rating proximity (items with similar ratings)

### Home Screen Rows

- The refresh task stores the recommended item IDs per user (`plugins/LocalRecs/recommendations.json`)
- The plugin registers two sections with Home Screen Sections; when a user's home screen is built, the sections return the real library items for exactly that user
- Items the user can no longer see, or has watched/started in the meantime, are filtered out at request time

### Netflix-style start page for Jellyfin Web (since 12.18.0)

In the browser and the Jellyfin apps for iOS and Android the start page can look like Netflix, with all rows of the TV app Quallenkino. Requires the plugin **File Transformation**. Turn it on in the settings („Startseite im Netflix-Stil“); the Quallenkino colors are a separate setting („Quallenkino-Farben“).

- Large hero with backdrop, logo, reason, "Play" and "More info"; a local trailer plays in the background (always muted, sound button on PCs). The header is transparent over the hero and turns dark when scrolling.
- Rows: continue watching, next up, recommended movies and series (with reason), today on TV for you, Top 10 (big numbers), worlds (opens the world page with filter and sorting), on now, "new in …" per library, genre rows and libraries. Loaded in parallel; the last start page is shown immediately from browser storage and refreshed row by row.
- PCs: arrows at the row edges and a preview card on hover; "More info" opens an overlay with episodes per season, similar titles and cast.
- Phones like the Netflix app: hero as a card with poster, logo and genres, chips, portrait posters, worlds as circles and a tab bar at the bottom.
- Netflix-style player (test, since 12.19.0) for selected users: own controls (back/title, big play/pause and ±10 s, progress bar with trickplay preview, audio & subtitles in one menu, episode list, speed, fullscreen, ⋮), Jellyfin keeps playing. „Klassischer Player“ under ⋮ per device.
- "Play" starts playback in the same browser/app. Every user can switch back to the classic start page at the bottom of the page. Files are loaded with the plugin version as query (`?v=…`), so apps with a web cache get updates right away.

### Live TV reminders and calendar (since 12.20.0)

Remember single programmes or whole series (all future broadcasts with the same title, episodes once). Remembered programmes appear in a per-user **iCal subscription** with an alarm before the start (default 10 min) – phones remind through their own calendar. Jellyfin Web: bell on live TV tiles and on the programme page („Merken“, „Serie merken“), profile menu „Erinnerungen im Kalender“ (subscribe, copy link, QR code, renew link).

`GET /LocalRecs/Reminders`, `POST|DELETE /LocalRecs/Reminders/{programId}` (204 / 404 / 409 when over), `GET /LocalRecs/Reminders/Series`, `POST /LocalRecs/Reminders/Series/{programId}` (200 with the series), `DELETE /LocalRecs/Reminders/Series/{key}`, `GET /LocalRecs/Calendar` → `{ Url, WebcalUrl, QrUrl }`, `POST /LocalRecs/Calendar/Renew`, `GET /LocalRecs/Calendar/Qr?target=webcal|https` (SVG), `GET /LocalRecs/Calendar/{token}.ics` (no sign-in, secret token). IDs with or without dashes; `/LocalRecs/Web/Config` reports `"Reminders": true`.

### API for TV apps

`GET /LocalRecs/Recommendations?type=movies|series|livetv&limit=20` returns the same items as the home screen rows for the **signed-in user** (regular token, no admin) as `{ "Items": [...], "TotalRecordCount": n }`. Works without Home Screen Sections.

Since 12.7.1 (requires the XMLTV source setting): `GET /LocalRecs/LiveTv/Program/{programId}/Credits` and `GET /LocalRecs/LiveTv/Credits?ids=a,b,c` (max. 200) return `{ "ProgramId", "Matched", "Actors": [{ "Name", "Role" }], "Directors", "Writers", "Year", "Country", "Episode", "ImdbId", "TmdbId" }` for live TV programs (regular token with live TV access). No XMLTV match: `Matched: false` (HTTP 200); unknown ID: 404 or left out of the map.

Since 12.10.0: **Worlds** like on Disney+ (Marvel, Star Wars, Pixar, …, matched by studios and Jellyfin collections, editable in the settings: one line per world `Name | Studios | Collections`, shown from 5 titles). `GET /LocalRecs/Worlds` lists the worlds of the signed-in user (recently watched first), `GET /LocalRecs/Worlds/{id}/Recommendations?limit=20` returns the titles of a world ordered by the user's taste (next part of a started film series first, watched/started/disliked titles removed), `GET /LocalRecs/Worlds/Recent` the world watched most recently with its titles (or 204). Since 12.10.1 each world lists its `TmdbCollectionIds` (e.g. to show missing parts via Jellyseerr). Since 12.11.0 the worlds come from a maintained list on GitHub (since 12.12.0 in the repository [Jellyfin-Welten](https://github.com/DrdotHouse2106/Jellyfin-Welten), updated weekly from TMDB; manual additions and exclusions live in its `regeln.json`), loaded daily; setting "Welten-Liste" can point to your own file. The "Welten" tab shows which titles belong to a world and why, with suggestions for missing titles.

Since 12.16.0 each world in `/LocalRecs/Worlds` has `TrailerItemId`, `TrailerItemName` and `TrailerId`: a title of the world with a **local trailer** (in a `Trailers/` folder or with a `-trailer` suffix, e.g. from Trailarr) and its first trailer, so apps can play it in the background like Disney+ (`/Videos/{TrailerId}/stream`). One of the seven most recently added titles with a trailer is used, changing daily (since 12.16.1) and stable within a day; the list can pin one per world (`"trailer": 671` as TMDB ID or `{ "imdb": "…", "typ": "film" }`). Empty without a local trailer. Since 12.15.0 `GET /LocalRecs/Worlds/{id}/Items?sort=year|year-asc|name|rating|added&type=movies|series&startIndex=&limit=` returns all titles of a world visible to the user (also watched ones, including titles from the list by TMDB/IMDb/TVDB ID). Since 12.14.0 a world can list several logos in order, including fanart.tv franchise logos (`fanart:collection/{id}`, needs your fanart.tv API key in the settings), and former IDs (`aliase`) keep working. Since 12.13.0 worlds with a logo in the list have `LogoUrl` (`/LocalRecs/Worlds/{id}/Logo`): the plugin fetches the image (https only), caches it on the server and serves it without sign-in.

Since 12.6.0: `GET /LocalRecs/Top10?type=all|movies|series&days=60&limit=10` returns the most watched movies and series of **all users** (more different viewers first, then plays; episodes count for their series). No admin rights needed; only titles visible to the signed-in user, no user names. **Thumbs up/down:** "like" counts like a favorite for watched titles (since 12.17.0 only weakly, 30 % without favorite boost, for never played titles, since Jellyfin Enhanced uses likes as a watchlist), "dislike" is never recommended and pulls similar titles down.

### Privacy

**100% local processing:**
- No external services or cloud dependencies
- No tracking or telemetry
- Only uses data already in your Jellyfin database
- All computation happens on your server

### Live TV

- **Variety and learning from behaviour (12.8.0):** titles recommended for more than 14 days without being watched slowly move back (2 % per day, down to half; setting "Ignorierte Empfehlungen abwerten nach", 0 = off). Movies stopped before a quarter of their runtime and not resumed for 3 days count as a weak dislike. At most a third of the movie/series row comes from the same main genre. "Heute im TV für dich" skips episodes already watched live or in the library.
- **Film series and reasons (12.9.0):** if a part of a Jellyfin collection was watched, the next unwatched part is put at the front of the movie row (at most a quarter). The home screen cards show why a title is recommended above its name, e.g. "Weil du „Frieren“ geschaut hast" or "Nächster Teil nach „Harry Potter 1“". Apps get the reasons via `GET /LocalRecs/Recommendations/Reasons` (item ID → text).
- **Ratings:** movies and series with a community rating below 5.0 are not recommended (unrated titles are kept; setting "Mindestbewertung", 0 = off). Well rated titles rank higher among similar matches ("Gewicht der Bewertung", default 0.15).
- **Actors from an XMLTV file (optional):** Jellyfin stores no people for live TV programs. Setting "XMLTV-Quelle für Darsteller" takes an XMLTV file (path inside the Jellyfin container or URL, `.xml` or `.xml.gz`). Actors (up to 10), directors, production year and country are matched to the programs by start time and title (channel IDs may differ; since 12.17.0 the start may differ by up to 15 minutes, preferring the same channel, then the nearest start), so favourite actors from live TV and from watched library movies count for "Heute im TV für dich". Reloaded on change or every 6 hours; a broken file keeps the last good state. A helper script that also adds Bundesliga pairings to the German epgshare01 EPG is in [`extras/epg-enrich`](extras/epg-enrich/).
- **Live TV troubleshooting:** the status on the Startseite tab shows playback events since server start (of which live TV) and the recently seen playbacks with user, client, type and title.
- Jellyfin only reports the channel during live TV playback; the plugin looks up the running program in the EPG and counts watched minutes per broadcast (all clients, including native TV apps).
- Channel switching (e.g. a football conference across several channels) counts minutes per game; since all games share the title/genre, the interest adds up.
- Regular shows (watched live on at least 3 different days, e.g. "Die Sendung mit der Maus") are always included, up to 7 days ahead.
- Cards show the broadcast time and channel ("Heute 20:15 · ZDF"); the row is chronological and at most a third comes from the same genre.
- Besides playback reports, active sessions are checked every minute, so clients that do not report progress are counted as well.
- Evaluation: recommended vs. watched per section (hit rate) in the "Auswertung" tab and via `GET /LocalRecs/Evaluation?days=30`.
- Views shorter than 3 minutes (zapping) and the genres "Nachrichten, News, Wetter" are ignored (configurable).
- "Heute im TV für dich" ranks programs of the next 24 hours by genre, title and people interest from live TV and library history; each show appears once (next airing).
- A title watched live for at least 15 minutes in total that also exists in the library counts as watched for the library recommendations.
- Stored locally in `plugins/LocalRecs/livetv-history.json` (default 180 days, configurable up to 3650 days).

## Known Limitations

- **Native TV apps:** Android TV, Fire TV and Roku apps do not render Home Screen Sections, so recommendations are not visible there (except in [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) for Android TV / Fire TV).
- **Requires Home Screen Sections:** without it the recommendations are computed but not shown.

## Troubleshooting

### The rows do not appear

- Check the plugin's **Startseite** tab: Home Screen Sections must be installed and the sections registered.
- Make sure Home Screen Sections is enabled and the two sections are switched on (admin settings or "Modular Home").
- Force-refresh the browser page (Ctrl+F5).
- If no recommendations were calculated yet, run Dashboard → Scheduled Tasks → "Refresh Local Recommendations".

## Building from Source

**Prerequisites:** .NET 10.0 SDK, Git

```bash
git clone https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin.git
cd Lokal-Vorschlag-Jellyfin

# Build (uses dotnet-helper.sh wrapper)
bash dotnet-helper.sh build

# Run tests
bash dotnet-helper.sh test

# Output: Jellyfin.Plugin.LocalRecs/bin/Debug/net10.0/
```

**Windows:** Use Git Bash or WSL to run the helper script.

## Contributing

Contributions welcome! See [DESIGN.md](DESIGN.md) for technical details and architecture.

## Support

- **Issues:** [GitHub Issues](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/issues)
- **Documentation:** [DESIGN.md](DESIGN.md)

## Data sources

Data and images: [TMDB](https://www.themoviedb.org). This product uses the TMDB API but is not endorsed or certified by TMDB.

World logos (optional): [fanart.tv](https://fanart.tv).

## Support the project

Development costs time and money (servers, test devices, AI tools). If you like the project, a small donation is very welcome:

[![Donate via PayPal](https://img.shields.io/badge/Donate-PayPal-0070ba?logo=paypal&logoColor=white)](https://paypal.me/DrdotHouse)

### 🎮 Got a gamer streak too?

Then my other project might be just right for you: **[ZockerDB](https://zockerdb.de)** – your game
collection in one place. Track games, consoles and accessories (including barcode scanning with your
phone), rate and manage them, with price history, editions and PAL/USK details. Use it directly at
[zockerdb.de](https://zockerdb.de) or self-host [ZockerDB Basis](https://github.com/DrdotHouse2106/ZockerDBBasis) on your own server.

## License

GNU General Public License v3.0 - see [LICENSE.txt](LICENSE.txt)
