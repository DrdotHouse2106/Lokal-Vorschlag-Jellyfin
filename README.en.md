# Quallenkinoplugin for Jellyfin

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

[![Buy me a coffee](https://img.shields.io/badge/☕_Buy_me_a_coffee-PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/DrdotHouse)

The **Quallenkinoplugin** (called "Local Recommendations" up to 12.27) adds privacy-first personalized recommendations based entirely on local watch history and metadata similarity (no cloud services, no tracking), live TV with an own programme guide provider, reminders, recording, "Wer schaut?" with profile images and a Quallenkino-style interface for Jellyfin Web. The TV app [Quallenkino](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) uses the same APIs.

> [!NOTE]
> **Based on / original:** This plugin is based on the original project **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** by Roger Pharr.
> This repository is a heavily extended version adapted for **Jellyfin 12**, as the original will not be updated for Jellyfin 12. All credit for the actual development goes to the original author.
>
> German version: [README.md](README.md)

Please report any issues or feedback on [GitHub Issues](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/issues).

> [!IMPORTANT]
> Since **12.2.0** recommendations are shown as **rows on the home screen** instead of per-user libraries. Since **12.48.0** Quallenkino draws the home screen itself – **the "Home Screen Sections" plugin is no longer needed** and can be uninstalled. Recommendation libraries created by older versions are removed automatically on first start. **Native apps** (Android TV, Fire TV, Roku, Swiftfin on iPhone/iPad/Apple TV) do not show these rows. For Android TV / Fire TV there is the customized app [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client), which loads the recommendations via `GET /LocalRecs/Recommendations` (since 12.5.0).

## Features

- **Per-user personalization** - Tailored recommendations for each user's viewing history
- **Content-based filtering** - TF-IDF embeddings with cosine similarity matching
- **Temporal similarity** - Decade-based grouping finds content from similar time periods
- **Home screen rows** - "Empfohlene Filme für dich" / "Empfohlene Serien für dich", individual per user
- **Real library items** - full metadata, shared watch state, no duplicates in "Continue Watching", no extra libraries
- **Live TV** - tracks which programs you watch live (all clients, including TV apps) and shows the row **"Heute im TV für dich"** with matching upcoming programs from the EPG
- **Own programme guide provider "LocalRecs EPG"** - replaces Jellyfin's XMLTV provider; rule blocks decide movie or series, season/episode are kept, recordings land in the right folder
- **Quallenkino-style Jellyfin Web** (browser, iOS and Android app) - start page with all rows, detail view, Quallenkino-style player, skip with countdown, tidy header and profile menu (since 12.47.0: „Profil wechseln“ and „Zufälliger Titel“ directly, Quallenkino functions under the expandable „Quallenkino“ entry, rarely used Jellyfin functions – SyncPlay, cast, Quick Connect – under „Mehr …“, „Diagnose senden“ only with diagnostics on)
- **"Wer schaut?"** - profile picker on start, one click to switch, no PIN
- **Profile images** - pick a figure and a background colour, the server renders the round image
- **Live TV reminders** - remember programmes and series, iCal calendar subscription with alarm, QR code for phones
- **Recording** - record a programme or a series right from the info view
- **Worlds like Disney+** and **"Top 10 bei euch"** - for apps like Quallenkino and the Quallenkino-style start page
- **APIs for TV apps** - everything also for own apps (Quallenkino for Android TV, Fire TV and webOS)
- **Metadata check** - shows per library how well movies and series are described, lists titles with little or too little metadata and refreshes just those
- **Privacy-first** - All processing happens locally on your server
- **Performance optimized** - Handles 2,000+ item libraries efficiently

## Requirements

- **Jellyfin Server:** 12.0+ (tested on 12.2)
- **.NET Runtime:** 10.0
- **Target ABI:** 12.0.0.0
- **For the home screen rows:** the plugin **File Transformation** by IAmParadox27 (repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`). Home Screen Sections and Plugin Pages are no longer needed since 12.48.0.
- **Clients:** web, desktop app, the Android app "Jellyfin" and the iOS app "Jellyfin Mobile" (both show the web interface). Native apps (Android TV, Fire TV, Roku, **Swiftfin** on iPhone/iPad/Apple TV) do not show the rows.

For Jellyfin 10.11.x use upstream version 0.6.1.

## Installation

1. **Add plugin repository:**  
   Dashboard → Plugins → Repositories → Add  
   `https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Plugins/main/manifest.json`  
   (the shared plugin repository [DrdotHouse2106/Jellyfin-Plugins](https://github.com/DrdotHouse2106/Jellyfin-Plugins); the old URL `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json` keeps working)

2. **Install plugin:**  
   Dashboard → Plugins → Catalog → Install **"Quallenkino"** (inside Jellyfin the plugin is just called "Quallenkino"; formerly "Local Recommendations" / "Quallenkinoplugin für Jellyfin" – an installed copy is simply updated, since 12.29 it removes its old folders on start)

3. **Restart Jellyfin server**

4. **Set up the home screen rows** (see Setup below)

Manual installation: download the newest `jellyfin-plugin-localrecs-v<version>.zip` from [`release/`](release/) (current version: top of the [CHANGELOG](CHANGELOG.md)), put `Jellyfin.Plugin.LocalRecs.dll` into a folder `LocalRecs_<version>` in Jellyfin's plugin directory (remove the old `LocalRecs_…` folder) and restart Jellyfin.

## Setup

1. Add the repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` and install **File Transformation**. Restart Jellyfin.
2. Done. The first calculation starts automatically after server start; afterwards the scheduled task "Empfehlungen aktualisieren" (before 12.26: "Refresh Local Recommendations") runs daily at 4:00 AM.

The plugin's **Startseite** tab shows the connection status and lets you change the row titles. Watched or started movies disappear from the row immediately. Every user only sees their own recommendations.

## Configuration

Access via: **Dashboard → Plugins → Quallenkino**. Tabs (German UI): **Startseite** (status, rows, live TV), **Einstellungen** (recommendations, Jellyfin Web, calendar, worlds), **Auswertung** (evaluation), **Welten** (worlds), **Fernsehprogramm** (LocalRecs EPG), **Aktionen** (actions).

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
- The web module draws the rows itself (since 12.48.0, no Home Screen Sections); each request returns the real library items for exactly that user
- Items the user can no longer see, or has watched/started in the meantime, are filtered out at request time

### Quallenkino-style start page for Jellyfin Web (since 12.18.0)

In the browser and the Jellyfin apps for iOS and Android the start page can look like the TV app Quallenkino, with all its rows. Requires the plugin **File Transformation**. Turn it on in the settings („Startseite im Quallenkino-Stil“); the Quallenkino colors are a separate setting („Quallenkino-Farben“).

- Large hero with backdrop, logo, reason, "Play" and "More info"; a local trailer plays in the background (always muted, sound button on PCs). The header is transparent over the hero and turns dark when scrolling.
- Rows: continue watching, next up, recommended movies and series (with reason), today on TV for you, Top 10 (big numbers), worlds (opens the world page with filter and sorting), on now, "new in …" per library, genre rows and libraries. Loaded in parallel; the last start page is shown immediately from browser storage and refreshed row by row.
- PCs: arrows at the row edges and a preview card on hover; "More info" opens an overlay with episodes per season, similar titles and cast.
- Phones like a streaming app: hero as a card with poster, logo and genres, chips, portrait posters, worlds as circles and a tab bar at the bottom.
- Quallenkino player (since 12.19.0, on for everybody by default since 12.29.0 – „Quallenkino-Player für alle Benutzer“, otherwise only the selected users; in the browser and the iOS and Android apps – since 12.31.2 it controls Jellyfin's player directly, also in the iOS app): own controls (back/title, big play/pause and ±10 s, progress bar with trickplay preview, audio & subtitles in one menu, episode list, speed, fullscreen, ⋮), Jellyfin keeps playing. „Klassischer Player“ under ⋮ per device.
- Diagnostics (since 12.30.0): switch „Diagnose: Fehler der Weboberfläche ins Serverlog schreiben“ (default off). Browsers and apps report script errors and, after a crash (e.g. the iOS app reloading the page), the last steps before it. Server log warning „Quallenkino-Diagnose [user · app version · device]“, access keys removed. Since 12.43.0 also the Jellyseerr results of **Jellyfin Enhanced** in the search: its `/JellyfinEnhanced/jellyseerr/…` requests (path, status and time only), its Seerr messages (quoted texts such as the search term removed), since 12.44.0 when its „Seerr“ section is inserted, moved or removed (with the calling code), rebuilds of the search page and address changes, and, after a typing pause, whether Enhanced is loaded, Jellyseerr and search results are on and the „Seerr“ section is visible with how many cards. Turn it off again afterwards.
- "Play" starts playback in the same browser/app. Every user can switch back to the classic start page at the bottom of the page. Files are loaded with the plugin version as query (`?v=…`), so apps with a web cache get updates right away.

### "Wer schaut?" – profile picker (since 12.21.0)

So that every person gets their own recommendations, Jellyfin Web shows a profile picker with avatars when it opens, like streaming services. Turn it on in the settings („Wer schaut?“ – Profilauswahl, default off; requires **File Transformation**).

- **No PIN:** each profile signs in once via „Profil hinzufügen“, afterwards one click switches. The other profiles stay signed in.
- Sign-ins are stored **only in that browser** (the same way Jellyfin stores its own sign-in), not on the server.
- The picker appears once per opened tab / app start and is always available via „Profil wechseln“ in the profile menu; Esc closes it.
- **Administrators are never remembered** (since 12.39.0): they sign in with their password each time, their sign-in is logged out when switching to another profile, and an administrator profile stored by older versions is removed and logged out on the next click.
- **Caution:** whoever gets to the browser can open every remembered profile without password – only turn it on for shared devices you trust.
- „Profile verwalten“ removes a profile from this browser and logs its sign-in out on the server (since 12.39.0; not the currently signed-in profile). Signing out via Jellyfin's „Abmelden“ invalidates the stored sign-in; the profile is then removed on the next click with a hint and can be added again.
- `/LocalRecs/Web/Config` reports `"ProfilesEnabled": true|false`.

### Profile images from figures (since 12.22.0)

Every user can build a profile image from a **figure** and a **background color**: in Jellyfin Web via „Profilbild wählen“ in the profile menu or „Wer schaut?“ → „Profile verwalten“. The server renders a round 512×512 PNG and sets it as the user's Jellyfin profile image. Users only change their own image; permissions stay unchanged (users without „change user preferences“ cannot change it). Quallenkino uses the same API.

Settings (Jellyfin Web): „Profilbilder aus Figuren wählen“ (default on), catalog (`icons.json`; empty = default from [Jellyfin-Welten/icons](https://github.com/DrdotHouse2106/Jellyfin-Welten/tree/main/icons), about 85 OpenMoji figures, CC BY-SA 4.0, also provides the color palette) and additional catalogs (one address per line, their categories are added).

Catalog format: `{ "format": 1, "name", "license", "colors": ["#RRGGBB"], "categories": [{ "id", "name", "icons": [{ "id", "name", "file" | "url", "fit": "contain" | "cover" }] }] }` – IDs `a–z0–9-_`, `file` relative to the catalog, PNG/JPEG/WebP up to 2 MB, transparent square PNGs recommended. The catalog is cached for 6 hours, images for 7 days.

API: `GET /LocalRecs/Avatars` (catalog, image URLs via the plugin, `Current` = last choice), `GET /LocalRecs/Avatars/Icons/{category}/{icon}` (image, no sign-in), `POST /LocalRecs/Avatars/Me {"Icon":"tiere/katze","Color":"#2e96e0"}` → `{ PrimaryImageTag }`. `/LocalRecs/Web/Config` reports `"Avatars"` and `"AvatarCatalogUrl"`.

### Programme guide provider "LocalRecs EPG" (since 12.26.0)

Jellyfin's built-in XMLTV provider marks a programme as a movie as soon as one category is in its „movie categories“ list. With e.g. „Komödie“ there, comedy series become movies: season/episode/episode title are dropped and recordings end up under `Movies/`. **LocalRecs EPG** is an own listings provider for XMLTV sources (URL or path, also `.gz`) that replaces it, keeping the channel mapping. Switchable rule blocks decide instead (tab „Fernsehprogramm“). In the dashboard (Live TV) Jellyfin shows own providers as „Unknown“ with a broken link; with the Quallenkino web module (since 12.38.0) it reads „LocalRecs EPG (Quallenkino)“ and opens this tab.

- **Series by episode number** – `episode-num` (xmltv_ns or on-screen like `S02E02`) → series episode with season, episode and episode title, whatever the categories.
- **Movie** – no episode number, at least 75 minutes (configurable) and one movie hint: director, IMDb link or a movie category (default „Spielfilm“); the production year alone only if switched on (shows and teleshopping often have one). Never for sports or news. If the programme source is a reduced XMLTV (e.g. m3u-editor) without director/year/IMDb, these come from the „XMLTV-Quelle für Darsteller“ (match by start ±15 min and title, channel IDs may differ) – since 12.27.0.
- **Series by sub-title** – no movie but a `sub-title` → series episode.
- **Never a movie** (since 12.29.0) – documentary/reportage/magazine categories, even with director and length.
- **Neither movie nor series** (since 12.29.0) – teleshopping by category or channel (HSE, QVC, 1-2-3.tv …).
- **Kids / news / sports** – by category (whole word) or channel (e.g. KiKA = always kids).

Genres are kept completely. Programme IDs (Id, ShowId, SeriesId) are built exactly like the built-in provider, so scheduled and series recordings survive the switch. Setup: choose the old provider to take over channel mapping, tuners and source, press „LocalRecs EPG einrichten“ (it is put first and the guide is refreshed), check with „Regeln testen“ (counts and examples with reasons), then remove the old provider. API (admins): `GET /LocalRecs/Epg/Providers`, `POST /LocalRecs/Epg/Setup {Path?, FromProviderId?}`, `POST /LocalRecs/Epg/Refresh`, `GET /LocalRecs/Epg/Test?search=&kind=&limit=&path=`.

### Live TV reminders and calendar (since 12.20.0)

Remember single programmes or whole series (all future broadcasts with the same title, episodes once). Remembered programmes appear in a per-user **iCal subscription** with an alarm before the start (default 10 min) – phones remind through their own calendar. Jellyfin Web: bell on tiles of upcoming programmes (start page and, since 12.24.0, Jellyfin's own live TV tab „Programme“), „An Sendung erinnern“ / „An Serie erinnern“ in the context menu, „Merken“ / „Serie merken“ in the guide's programme dialog and the info view, „Aufnehmen“ / „Serie aufnehmen“ with status in the info view and a record button on the preview card (since 12.25.0, Jellyfin's recording defaults, only for users allowed to manage recordings), profile menu „Erinnerungen im Kalender“ (subscribe, copy link, QR code, renew link). Since 12.24.0 Jellyfin's programme rows (series, movies, sports, kids, news) only show upcoming programmes sorted by start; airing ones stay in „Gerade läuft“.

`GET /LocalRecs/Reminders`, `POST|DELETE /LocalRecs/Reminders/{programId}` (204 / 404 / 409 when over), `GET /LocalRecs/Reminders/Series`, `POST /LocalRecs/Reminders/Series/{programId}` (200 with the series), `DELETE /LocalRecs/Reminders/Series/{key}`, `GET /LocalRecs/Calendar` → `{ Url, WebcalUrl, PageUrl, QrUrl }`, `POST /LocalRecs/Calendar/Renew`, `GET /LocalRecs/Calendar/Qr?target=page|webcal|https` (SVG, default `page` since 12.23.0 – the iPhone camera does not open webcal links), `GET /LocalRecs/Calendar/{token}.ics` (no sign-in, secret token), `GET /LocalRecs/Calendar/{token}/Abonnieren` (subscribe page: webcal button, Google Calendar link, copy). An empty calendar contains an invisible placeholder (2000-01-01) because iOS rejects subscriptions without events. IDs with or without dashes; `/LocalRecs/Web/Config` reports `"Reminders": true`.

### API for TV apps

Banner of the day ("Billboard", since 12.41.0): `GET /LocalRecs/Billboard?limit=5&utcOffsetMinutes=120` (same: `/LocalRecs/Banner?count=5`) returns movies and series with backdrop for the big banner of the signed-in user, in display order, plus `"Reasons": { "<Id>": "Weiterschauen" | "Neue Folgen" | "Fortsetzen" }` for titles that fit right now; `/LocalRecs/Web/Config` reports `"Billboard": true`. Since 12.42.0 about 60 % come from the best 30 recommendations and about 40 % are started series with unwatched (also new) episodes and started movies, alternating. Weighted at random with date + user as seed – the same on every device during a day, new the next day (midnight in the app's time zone). Weekday evenings (Sun–Thu, 17–22) count movies over 100 minutes half. Less often: titles from the banner of the last days (yesterday ×0.15, two days ago ×0.3, within a week ×0.6), titles shown on several days but never opened (×0.5 / ×0.25) and recommendations ignored in the rows for long (×0.5 / ×0.3). „Später“: `POST /LocalRecs/Billboard/Later` with `{ "ItemId", "Days" }` (default 21) takes a title out of the banner without touching the recommendations; `GET` lists, `DELETE /LocalRecs/Billboard/Later/{itemId}` brings it back. Stored in `banner.json` and `banner-spaeter.json` (title IDs only).

Identity proof in the home network (since 12.35.0): `GET /LocalRecs/Identity/Challenge?nonce=<64 lower-case hex>` with header `X-Device-Id` returns `{ "ServerId", "Host", "Proofs": [...] }`, `Proof = hex(HMAC-SHA256(key = SHA-256(access token), msg = "nonce|ServerId|host"))`, host = address the app connected to (lower-case, with port). The app sends its token over http only if its own proof is listed. Since 12.39.0 `Proofs` always holds at least 4 entries in random order (filled with random values), so the answer does not reveal whether a device is known. Anonymous, but only for direct connections from the local network (no forwarding headers), otherwise 403; 30 requests per minute and address.

`GET /LocalRecs/Recommendations?type=movies|series|livetv&limit=20` returns the same items as the home screen rows for the **signed-in user** (regular token, no admin) as `{ "Items": [...], "TotalRecordCount": n }`.

Since 12.7.1 (requires the XMLTV source setting): `GET /LocalRecs/LiveTv/Program/{programId}/Credits` and `GET /LocalRecs/LiveTv/Credits?ids=a,b,c` (max. 200) return `{ "ProgramId", "Matched", "Actors": [{ "Name", "Role" }], "Directors", "Writers", "Year", "Country", "Episode", "ImdbId", "TmdbId" }` for live TV programs (regular token with live TV access). No XMLTV match: `Matched: false` (HTTP 200); unknown ID: 404 or left out of the map.

Since 12.10.0: **Worlds** like on Disney+ (Marvel, Star Wars, Pixar, …, matched by studios and Jellyfin collections, editable in the settings: one line per world `Name | Studios | Collections`, shown from 5 titles). `GET /LocalRecs/Worlds` lists the worlds of the signed-in user (recently watched first), `GET /LocalRecs/Worlds/{id}/Recommendations?limit=20` returns the titles of a world ordered by the user's taste (next part of a started film series first, watched/started/disliked titles removed), `GET /LocalRecs/Worlds/Recent` the world watched most recently with its titles (or 204). Since 12.10.1 each world lists its `TmdbCollectionIds` (e.g. to show missing parts via Jellyseerr). Since 12.11.0 the worlds come from a maintained list on GitHub (since 12.12.0 in the repository [Jellyfin-Welten](https://github.com/DrdotHouse2106/Jellyfin-Welten), updated weekly from TMDB; manual additions and exclusions live in its `regeln.json`), loaded daily; setting "Welten-Liste" can point to your own file. The "Welten" tab shows which titles belong to a world and why, with suggestions for missing titles.

Since 12.16.0 each world in `/LocalRecs/Worlds` has `TrailerItemId`, `TrailerItemName` and `TrailerId`: a title of the world with a **local trailer** (in a `Trailers/` folder or with a `-trailer` suffix, e.g. from Trailarr) and its first trailer, so apps can play it in the background like Disney+ (`/Videos/{TrailerId}/stream`). One of the seven most recently added titles with a trailer is used, changing daily (since 12.16.1) and stable within a day; the list can pin one per world (`"trailer": 671` as TMDB ID or `{ "imdb": "…", "typ": "film" }`). Empty without a local trailer. Since 12.15.0 `GET /LocalRecs/Worlds/{id}/Items?sort=year|year-asc|name|rating|added&type=movies|series&startIndex=&limit=` returns all titles of a world visible to the user (also watched ones, including titles from the list by TMDB/IMDb/TVDB ID). Since 12.14.0 a world can list several logos in order, including fanart.tv franchise logos (`fanart:collection/{id}`, needs your fanart.tv API key in the settings), and former IDs (`aliase`) keep working. Since 12.13.0 worlds with a logo in the list have `LogoUrl` (`/LocalRecs/Worlds/{id}/Logo`): the plugin fetches the image (https only), caches it on the server and serves it without sign-in.

Since 12.6.0: `GET /LocalRecs/Top10?type=all|movies|series&days=60&limit=10` returns the most watched movies and series of **all users** (more different viewers first, then plays; episodes count for their series). No admin rights needed; only titles visible to the signed-in user, no user names. **Thumbs up/down:** "like" counts like a favorite for watched titles (since 12.17.0 only weakly, 30 % without favorite boost, for never played titles, since Jellyfin Enhanced uses likes as a watchlist), "dislike" is never recommended and pulls similar titles down.

### Behavior: how far, when, what was searched (since 12.36.0)

- Rewatches count more (twice 1.25, four times 1.75); started movies (at least a quarter, not abandoned) count as weak interest; series count by the share of watched episodes (0.6–1.4).
- Time of day: a profile of what a user watches in the morning (5–11), daytime (11–17), evening (17–22) and night, from play times. The recommendation rows movies/series/live TV of the Quallenkino start page follow it, and genres typical for the time of day move titles up a little (weight 0.15). From 15 plays per daypart; time zone from the browser (`utcOffsetMinutes`).
- Searches and opened detail pages count as interest (weight 0.35, a third after the „Nachwirkung“ of 30 days; repeated interest in the same title adds up), with reasons „Weil du nach „…“ gesucht hast“ / „Weil du dir „…“ angesehen hast“. Stored only on the server (`verhalten.json`), by default 365 days and at most 1000 entries each (adjustable: 7–1095 days, 50–5000 entries). Each user sees and controls it in the profile menu „Was Quallenkino sich merkt“ (switch off deletes everything); admins can switch both off.
- API: `POST /LocalRecs/Behavior/Search {Term}`, `POST /LocalRecs/Behavior/View {ItemId}`, `GET /LocalRecs/Behavior/Daypart?utcOffsetMinutes=`, `GET/POST/DELETE /LocalRecs/Behavior/Me`; `GET /LocalRecs/Recommendations` takes `utcOffsetMinutes`.

### Similar titles from TMDB and Trakt (since 12.33.0)

Public similarity lists are an extra signal on top of the own content similarity – mostly for users with little watch history (there the bonus counts twice):

- TMDB "recommendations" and "similar" per title, optionally Trakt "related" (needs an own Trakt app, client ID). Library titles named as similar to watched or liked titles get a bonus (weight 0.25 by default); card reason „Ähnlich wie „…““.
- Only the TMDB/IMDb ID of a title is sent – no users, no watch history. The whole library is looked up bit by bit (watched titles first), so the requests reflect the library, not what was watched. Cache 7 days, at most 400 titles per run. Key: own TMDB key (v3 or v4 read token), otherwise the key in Jellyfin's TMDb settings or Jellyfin's built-in key – the settings page shows which one is used; switch „Ohne eigenen Schlüssel den von Jellyfin mitbenutzen“ turns that off. Without key or on errors it is silently skipped.
- **Personal Trakt recommendations** for users with a Trakt account (`/recommendations/movies` and `/shows`, weight 0.5, reason „Für dich bei Trakt empfohlen“): own login in Quallenkino (profile menu „Trakt-Empfehlungen“, device code; needs client ID and secret of an own Trakt app) or – read only – the link of the Trakt plugin (`Trakt.xml`, never refreshed or written back). Off by default (since 12.34.0): only users who switch it on themselves; the Trakt plugin's link is read only after that. Only the recommendation list is fetched every 12 hours, nothing is sent to Trakt. API: `GET/POST /LocalRecs/Trakt/Me`, `POST/DELETE /LocalRecs/Trakt/Me/Login`.
- Concept for voluntarily shared watch histories (not built yet): [docs/konzept-gemeinsame-empfehlungen.md](docs/konzept-gemeinsame-empfehlungen.md) (German).

### Movie series for you (since 12.45.0)

Own row for **movie series** (TMDB collections such as John Wick or Terminator), separate from the worlds. Only series with at least two parts in the user's libraries (respecting libraries and parental ratings, duplicates counted once, finished series left out). Started series first (most recently watched first, card „x von y gesehen · Weiter mit Teil n“), then series that fit the taste (position of a part in the movie recommendations, otherwise most watched genres); series forming a world (James Bond, Harry Potter) last in each group. A click shows all parts in release order; a movie's info view lists „Weitere Teile der Reihe“. Membership comes from the movie's TMDB collection, Jellyfin collections with TMDB ID, or a nightly TMDB lookup (`movie/{id}` → `belongs_to_collection`, 400 movies per run, cached 60 days in `filmreihen.json`, only the movie's TMDB ID is sent). Poster and backdrop are cached on the server. API: `GET /LocalRecs/MovieSeries?limit=12`, `GET /LocalRecs/MovieSeries/{id}`, `GET /LocalRecs/MovieSeries/ByItem/{itemId}`, `GET /LocalRecs/MovieSeries/{id}/Images/Poster|Backdrop`; `/LocalRecs/Web/Config` reports `"MovieSeries"` and `"MovieSeriesTitle"`.

### Seasonal row (since 12.45.0)

Christmas movies from the first Sunday of Advent to 26 December, New Year's Eve 27 December–1 January, Halloween 15–31 October, Easter Good Friday–Easter Monday (Gauss), optionally Valentine's Day 7–14 February. Detected by TMDB keywords (nightly, 400 titles per run, cached 180 days, only the TMDB ID is sent), equal Jellyfin tags and whole words in titles („Adventure“ is not Advent), plus episodes and specials with such titles (at most 3 per series). Each user only sees titles of their libraries and within their parental rating. Unwatched first, then taste and rating, at most 20; shown from 4 titles (adjustable). API: `GET /LocalRecs/Seasonal?utcOffsetMinutes=120` → `{ "Active", "Id", "Title", "Items" }`, `?season=weihnachten` previews a season; `/LocalRecs/Web/Config` reports `"Seasonal"`.

### Time-based recommendations (since 12.46.0)

**Weekday and time left in the evening:** besides the time of day, each profile learns whether weekday evenings get less long content (over 100 minutes) than weekend evenings (from 10 plays per slot; default: Friday/Saturday evenings long, other evenings short) and its usual bedtime (80th percentile of the last activity per evening, from 5 evenings, otherwise 23:30). From 19:00, what no longer fits moves back – in the recommendation rows and the banner. Until a daypart is learned, family/animation move up in the daytime and horror/thriller at night. Tab „Einstellungen“, weight 0.3.

**New rows** (switch each off under „Zeitabhängige Zeilen“; libraries and parental ratings respected): „Läuft heute im TV – hier ohne Werbung“ (titles in the library airing on live TV until 02:00, matched by IMDb/TMDB/TVDB or title and year, only with live TV access), „Neu diese Woche“ (7–30 days, default 14, by taste), „Vor dem Kinostart: Hol die Reihe nach“ (a new part of a movie series in cinemas within 4 weeks – the next unwatched part, or the last one to refresh; release in the server's metadata country, otherwise Germany; parts looked up weekly at TMDB, only IDs sent), „Heute vor X Jahren“ (released 10, 20, 25, 30 … years ago today, at most 6), „Vor einem Jahr hast du …“ (watched 330–400 days ago). Left out on purpose: actor birthdays, events, school holidays, „leaving soon“. API: `GET /LocalRecs/Moments?utcOffsetMinutes=120` → `{ "Rows": [ { "Key", "Title", "Items", "Reasons" } ] }` (keys `ontv`, `newthisweek`, `cinema`, `anniversary`, `yearago`; `?key=` for one row; cached 10 minutes); `/LocalRecs/Web/Config` reports `"Moments"`.

Seasonal row since 12.46.0: movies and series before episodes, one episode per series first; specials (season 0) only when the file name agrees (TVDB/TMDB numbering of specials often differs).

### Banner on phones (since 12.50.0)

The portrait banner card shows the backdrop cropped to portrait with logo or title on top; only without a backdrop the poster, then without logo or title (posters carry the title).

### Upcoming (since 12.48.0)

Row **„Demnächst“** from **Sonarr** and **Radarr**, read only (nothing is requested or monitored): new episodes of series in Jellyfin within 14 days (1–60, matched by TVDB/TMDB/IMDb or title, one card per series with the next episode) and monitored movies not yet downloaded with a cinema, digital or disc release within 30 days (1–90, earliest date counts, images via the server). Since 12.49.0 every card names its date (landscape cards also on phones): `ReleaseType` `NewEpisode` „Neue Folge am Fr. 24.10.“, `SeasonPremiere` „Staffelstart am …“, `SeriesPremiere` „Serienstart am …“, `Cinema` „Kinostart am …“, `Digital` „Digital ab …“, `Physical` „Auf Disc ab …“, with „heute“/„morgen“ where it fits. A click shows the episode (SxxEyy, title, day, time, network, overview, further new episodes) with „Zur Serie“, or the movie with its date. Sorted by date; on the same day favourite and recently watched series first. Series only from the profile's libraries; movies only if Radarr's root folder matches one of the profile's movie libraries by folder name (otherwise only profiles with all movie libraries), parental rating checked. Settings: tab „Startseite“ → addresses, API keys (sent only as `X-Api-Key`, never logged, not in backups), periods, „Verbindung prüfen“; calendars cached 30 minutes. API: `GET /LocalRecs/Upcoming?utcOffsetMinutes=` → `{ "Items": [ { "Kind": "Episode"|"Movie", "ReleaseType", "Title", "Subtitle" (ready card text), "Date" (UTC), "LocalDate" (yyyy-MM-dd in the app's time zone), "Network", "EpisodeOverview", "Series", "SeasonNumber", "EpisodeNumber", "EpisodeTitle", "MoreEpisodes", "Movie": { "RadarrId", "TmdbId", "ImdbId", "Year", "Overview", "OfficialRating", "PosterUrl", "BackdropUrl" } } ] }`, images `LocalRecs/Upcoming/Movies/{id}/Images/Poster|Backdrop`, `GET /LocalRecs/Upcoming/Status` (admins), `/LocalRecs/Web/Config` → `"Upcoming"`.

### Upcoming episodes on the series page (since 12.52.0)

The series detail page shows below the facts what Sonarr knows next: „Neue Folge heute · 21:00 Uhr · Paramount+ · S02E05 Title“, „Staffelstart morgen · …“, „Nächste Folge am Fr. 31.10. · …“ plus „+2 weitere bis 06.11.“ (further episodes within 30 days), and „Neu: S02E05“ for episodes that aired today and are already in the library (marked NEU in the episode list). The next known episode counts without time limit; specials only if nothing else comes. Same rights as „Demnächst“, read only; Sonarr's series list cached 6 hours, a series' episodes 30 minutes. API: `GET /LocalRecs/Upcoming/Series/{seriesId}?utcOffsetMinutes=` → 204 if Sonarr is not set up, does not know the series or the user cannot see it; otherwise `{ "Next": { "SeasonNumber", "EpisodeNumber", "EpisodeTitle", "EpisodeOverview", "Date", "LocalDate", "ReleaseType", "Network" }, "Text", "MoreEpisodes", "MoreUntil", "MoreText", "NewToday": [ { …, "ItemId" } ], "NewText" }`.

### Without Home Screen Sections (since 12.48.0)

Quallenkino no longer registers with Home Screen Sections, which can be uninstalled; it starts without errors without it. „Jellyfin-Startseite anzeigen“ (formerly „Klassische Startseite anzeigen“) shows Jellyfin's own home page per profile and device. The row titles and the „Heute im TV für dich“ switch now apply to the Quallenkino home (`MoviesTitle`, `SeriesTitle`, `LiveTvTitle`, `LiveTvRow` in `/LocalRecs/Web/Config`). The sidebar group „Jellyfin Enhanced“ („Enhanced Panel“) is hidden; the panel stays available via „Jellyfin Enhanced“ in the profile menu.

### Metadata check (since 12.38.0)

Tab **„Metadaten“** in the plugin settings checks all movies and series: per library the number of titles without genres, TMDB/IMDb ID, tags, cast, director (movies), year, rating, overview or image. **Too little** = genres or ID missing, **little** = tags or cast missing or several minor gaps. A list of these titles (worst first, up to 500) with „Neu abrufen“ per title or for all shown (at most 200): the normal Jellyfin refresh in the background, existing metadata and images stay. Titles without ID usually need „Identify“ in Jellyfin. API (admins only): `GET /LocalRecs/Metadata/Check`, `POST /LocalRecs/Metadata/Refresh` with `{"ItemIds": [...]}`.

### Performance and visibility (since 12.34.0)

- Worlds: what a user may see comes from one library query, cached for 15 minutes and renewed at once when the user's access changes; last watched 5 minutes; the world membership is computed by one request only (single flight).
- Top 10: ranking cached server-wide per type and period for 10 minutes; per user only the visibility filter.
- Similar titles (TMDB/Trakt) are fetched after the recommendations are stored.
- Fixed: worlds and Top 10 showed titles from libraries the user has no access to (Jellyfin's `IsVisible` does not check library access).
- Configuration backups contain no keys (TMDB, Trakt client secret, fanart.tv); restore via `GET /LocalRecs/Config/Backups` and `POST /LocalRecs/Config/Backups/{name}/Restore` (admin) keeps the current keys.

### Privacy

- All computation happens on your server; no tracking or telemetry
- Outgoing: only title IDs to TMDB/Trakt (similar titles, see above) and, for users who switched it on, the request of their Trakt recommendation list
- Configuration backups without keys
- Searches and opened titles stay on the server (365 days by default, adjustable), each user can see, switch off and delete them

### Live TV

- **Variety and learning from behaviour (12.8.0):** titles recommended for more than 14 days without being watched slowly move back (2 % per day, down to half; setting "Ignorierte Empfehlungen abwerten nach", 0 = off). Movies stopped before a quarter of their runtime and not resumed for 3 days count as a weak dislike. At most a third of the movie/series row comes from the same main genre. "Heute im TV für dich" skips episodes already watched live or in the library.
- **Film series and reasons (12.9.0):** if a part of a Jellyfin collection was watched, the next unwatched part is put at the front of the movie row (at most a quarter). The home screen cards show why a title is recommended above its name, e.g. "Weil du „Frieren“ geschaut hast" or "Nächster Teil nach „Harry Potter 1“". Apps get the reasons via `GET /LocalRecs/Recommendations/Reasons` (item ID → text).
- **Ratings:** movies and series with a community rating below 5.0 are not recommended (unrated titles are kept; setting "Mindestbewertung", 0 = off). Well rated titles rank higher among similar matches ("Gewicht der Bewertung", default 0.15).
- **Actors from an XMLTV file (optional):** Jellyfin stores no people for live TV programs. Setting "XMLTV-Quelle für Darsteller" takes an XMLTV file (path inside the Jellyfin container or URL, `.xml` or `.xml.gz`). Actors (up to 10), directors, production year and country are matched to the programs by start time and title (channel IDs may differ; since 12.17.0 the start may differ by up to 15 minutes, preferring the same channel, then the nearest start), so favourite actors from live TV and from watched library movies count for "Heute im TV für dich". Reloaded on change or every 6 hours; a broken file keeps the last good state. Since 12.40.0 at most 250 MB (unpacked) are read. A helper script that also adds Bundesliga pairings to the German epgshare01 EPG is in [`extras/epg-enrich`](extras/epg-enrich/).
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

- **Native TV apps:** Android TV, Fire TV and Roku apps do not show the rows (except [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) for Android TV / Fire TV, which uses the `/LocalRecs` API).
- **Requires File Transformation** for the rows in Jellyfin Web.

## Troubleshooting

Each entry: symptom, cause, fix. Kept up to date.

- **Plugin not in the catalog:** add the repository `https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Plugins/main/manifest.json` (remove the old single repository if the plugin shows up twice).
- **Nothing changes after install/update:** Jellyfin loads plugins only at startup – restart the server (check `GET /Sessions` first that nobody is watching), then reload the browser (Ctrl+F5) or pull down in the iOS app.
- **All `/LocalRecs` addresses return 500 after an update:** two versions are loaded – uninstall the old one under Dashboard → Plugins and restart again.
- **Settings gone after an update:** restore a backup from `plugins/configurations/Jellyfin.Plugin.LocalRecs.sicherungen/` with `POST /LocalRecs/Config/Backups/{name}/Restore` (keys are kept).
- **The rows do not appear:** the plugin's **Startseite** tab shows what is missing (File Transformation; „Jellyfin-Startseite anzeigen“ switches the Quallenkino home off per profile and device); run Dashboard → Scheduled Tasks → "Empfehlungen aktualisieren". Native apps (Android TV, Fire TV, Roku, Swiftfin) never show the rows.
- **Quallenkino interface missing:** File Transformation installed? Jellyfin Web switches on? Reload (Ctrl+F5 / pull down in the iOS app).
- **Banner always shows the same titles:** before 12.41.0 it took the top recommendations; since then it changes daily (stable within a day and an open tab). Use „Später“ for titles that don't fit now; titles without backdrop never appear – see the metadata check.
- **Recommendations look stale, task "Aborted":** a restart interrupted the nightly run – start the task by hand (data older than 24 h is recomputed at the next start anyway).
- **„Vor dem Kinostart“ never appears:** needs a TMDB key; parts are looked up weekly – run "Empfehlungen aktualisieren" once by hand.
- **„Läuft heute im TV“ stays empty:** check the profile's live TV access and that the guide covers tonight; listings need a shared ID or the same title (movies: year ±1).
- **Home page loads forever after signing out and in as another user (up to 12.52.0):** fixed in 12.52.1; before, reload the page.
- **Poor recommendations:** run the **Metadaten** check, refresh incomplete titles and identify unmatched ones.
- **No Jellyseerr results in the search:** they come from **Jellyfin Enhanced**, not from Quallenkino – Jellyseerr and its search results must be on there, reachable from the server, and the user linked in Jellyseerr (`GET /JellyfinEnhanced/jellyseerr/status` → `{"active":true}`). A case in the iOS app „Jellyfin Mobile“ where the section is rendered and then disappears (desktop fine) is still being investigated; use **Diagnostics** to see what happens.
- **Series recorded as movies:** use the provider **„LocalRecs EPG“** and its rules (tab „Fernsehprogramm“), then refresh the guide data. Long documentaries or travel shows taken as movies: add their category to **„Nie Film“**.
- **Provider shows as "Unknown" with a broken link in the dashboard:** fixed since 12.38.0 with the Quallenkino interface; otherwise open the plugin settings → „Fernsehprogramm“.
- **No cast/director/year for programmes:** set an **„XMLTV-Quelle für Darsteller“** (path or URL, `.xml`/`.xml.gz`, at most 250 MB unpacked); optional, the plugin does not wait for it at startup.
- **Calendar link points to an internal address:** set the public address for calendar links or add the proxy under Networking → Known proxies.
- **No personal Trakt recommendations:** off by default per user – each user switches them on in the profile menu; an own connection needs a Trakt app (client ID/secret) set by the admin. Tokens stay on the server in `trakt-konten.json` (mode 600), never in the browser, logs or backups; „Verbindung trennen“ deletes and revokes them.
- **„Ähnlich wie …“ stays empty:** no TMDB key (the settings show which key is used), TMDB not reachable, or titles lack TMDB/IMDb IDs.
- **Diagnostics:** settings → „Diagnose: Fehler der Weboberfläche ins Serverlog schreiben“ → save; reload the affected app, reproduce, search the server log for „Quallenkino-Diagnose“. Access keys and search terms are removed. Switch it off again afterwards.
- **Administrator must enter the password in „Wer schaut?“ every time:** intended since 12.39.0 – use a non-admin profile for everyday use.

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
