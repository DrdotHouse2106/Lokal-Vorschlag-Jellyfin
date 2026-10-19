# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [12.18.6] - 2026-10-19

### Added

- Netflix-style details page: Jellyfin's detail buttons are shown in the new design – mark as watched, trailer, shuffle, instant mix, download, split versions, cancel recording and the ⋮ menu (add to collection/playlist, download, copy stream link, delete, edit metadata/images/subtitles, identify, media info, refresh). They trigger Jellyfin's own buttons, so permissions, confirmations and menus stay Jellyfin's; menus open at the new button.

## [12.18.5] - 2026-10-19

### Added

- Jellyfin Web: the details page of movies and series (also when opened from a library, the search or on phones) uses the Netflix-style view like the overlay on the start page: large backdrop with trailer, logo, „Abspielen“, my list, like, cast, seasons with episodes, similar titles. „Alle Optionen“ switches to Jellyfin's own details page (versions, audio/subtitles, editing …). Other types (episodes, people, collections …) keep Jellyfin's page.

## [12.18.4] - 2026-10-19

### Fixed

- **Black picture during playback** with the Quallenkino colors: the page backgrounds covered the video. They are no longer applied while Jellyfin makes the document transparent for video (`html.transparentDocument`).

### Added

- Skip like in Quallenkino: during an intro, outro, recap or preview segment (media segments, e.g. from Intro Skipper) a button „Intro überspringen“ / „Abspann überspringen“ … appears with a 5-second progress fill and then skips automatically. Cancel with × , Esc or Back. Uses Jellyfin's own skip (correct with transcoding) when available, otherwise seeks the video. Active when the start page or the colors are on; also on phones.
- Player in Netflix look (with the Quallenkino colors): dark gradients at the top and bottom, bigger title, white controls, slim position slider with the gradient, Jellyfin's skip button in the same style.

## [12.18.3] - 2026-10-19

### Fixed

- Profile menu: the moved header buttons are rebuilt every time the menu opens, so buttons added later by other plugins (e.g. the dice "random title" button of Jellyfin Enhanced, also when that plugin has hidden it in its own tray) are included; no duplicates.
- Phones/iOS app: the header is watched there too, so plugin buttons added later (dice) are moved into the profile menu.
- Profile menu entries look the same (light text, 44 px rows, light hover); Jellyfin's own entries are no longer blue.
- Search icon in the header is white like the others.
- Quallenkino colors for the side menu of Jellyfin 12 (MUI drawer, also in the iOS app): dark background, white entries, active entry with the gradient.

## [12.18.2] - 2026-10-19

### Changed

- Jellyfin Web: tidy header like Netflix (when the start page is on). PCs: logo with Startseite, Serien, Filme, Live-TV, Meine Liste and „Bibliotheken ▾“ (all libraries); on the right only cast, search and profile. Phones: menu, search and profile. SyncPlay and icons of other plugins move to the top of the profile menu (they still open their own menus).
- Top 10: the big number lies over the bottom left of the poster, as on Netflix; on phones about 3 titles per screen width.
- Phones: the hero card got an „Info“ button; the trailer fills the card (letterbox bars are cropped).
- iOS app: no own tab bar at the bottom (the app has its own bar); Safari and Android keep it.

## [12.18.1] - 2026-10-19

### Fixed

- Jellyfin Web start page: no more double titles on landscape tiles – tiles with logo/title overlay use the backdrop (no text in the image); thumb images, which usually contain the title already, are shown without overlay.
- Hero: logo limited to about 38 % width and 160 px height; the description stays visible while the trailer plays (no more gap between info line and buttons).
- Genre rows: German genre names preferred, only genres with at least 8 titles, more candidates are tried so that rows with few titles are left out.
- The reason ("Weil du … geschaut hast") only appears in the recommendation rows, not under the same title in other rows.
- Tiles without image (e.g. folders or libraries) get a gradient with the title.
- Top 10: the number stands next to the poster and is only slightly overlapped.

## [12.18.0] - 2026-10-19

### Added

- **Start page in Netflix style for Jellyfin Web** (browser and the Jellyfin apps for iOS/Android), injected via the plugin "File Transformation" (setting „Startseite im Netflix-Stil“, off by default):
  - large hero with backdrop, logo, reason, „Abspielen“/„Weitere Infos“ and the local trailer (always muted; sound button only on PCs), header transparent over the hero and dark when scrolling;
  - all rows of the TV app Quallenkino, loaded in parallel: Weiterschauen, Als Nächstes, recommended movies/series with reason, Heute im TV für dich, Top 10 with big numbers, Welten (tile → world page with filter and sorting), Jetzt im TV, „Neu in …“ per library, random genre rows, libraries;
  - PCs: arrows at the row edges and a preview card on hover (play, my list, like, more info); „Weitere Infos“ opens a detail overlay (episodes per season, similar titles, cast) instead of a new page;
  - phones like the Netflix app: hero as a card with poster, logo and genres, „Abspielen“/„Meine Liste“, chips „Serien“/„Filme“/„Meine Liste“, portrait posters (about 3.3 per screen), worlds as circles, tab bar at the bottom (Startseite, Suche, Live-TV, Meine Liste, Mehr);
  - the last start page is shown immediately from the browser storage and refreshed row by row in the background; „Abspielen“ starts playback in the same browser/app (remote command to the own session, fallback: details page);
  - every user can switch back to the classic start page at the bottom of the page.
- Setting „Quallenkino-Farben“: dark blue with the violet-blue gradient for all of Jellyfin Web, including side menu, playback bar and buttons.
- Script and stylesheet are loaded with the plugin version as query (`?v=…`), so apps with a web cache (e.g. the iOS app) get updates right away.

### Changed

- Recommendations (`/LocalRecs/Recommendations`, Home Screen Sections) now include logos and genres.

## [12.17.0] - 2026-10-18

### Changed

- XMLTV matching tolerates start times differing by up to ±15 minutes (guides often shift by a few minutes, e.g. "Fack Ju Göhte" 07:20 in Jellyfin vs. 07:25 in XMLTV). With several candidates the same channel wins (channel ID and display names compared loosely with the Jellyfin channel name), then the same episode title, then the nearest start.
- "Gefällt mir" on never played titles counts only weakly (factor 0.3, no favorite boost) and is no source for "Weil du … geschaut hast". Jellyfin Enhanced sets likes as a watchlist for requested titles; such likes no longer dominate the taste profile. Likes on watched titles and dislikes are unchanged.

## [12.16.1] - 2026-10-18

### Changed

- World trailers rotate daily among the seven most recently added titles with a local trailer (stable within a day); a title pinned in the list always wins. The choice is cached per user and world, so `GET /LocalRecs/Worlds` no longer searches for trailers on every call.

## [12.16.0] - 2026-10-18

### Added

- **World trailers** like on Disney+: `GET /LocalRecs/Worlds` returns `TrailerItemId`, `TrailerItemName` and `TrailerId` per world – a visible title of the world with a local trailer (Jellyfin extras: `Trailers/` folder or `-trailer` suffix, e.g. from Trailarr) and its first trailer. Default is the most recently added title with a trailer; the world list can pin a title per world with `"trailer"` (TMDB ID, or an object with `tmdb`/`imdb`/`tvdb`/`typ`).

## [12.15.1] - 2026-10-18

### Added

- Sorting `added` (recently added to the library first) for `GET /LocalRecs/Worlds/{id}/Items`.

## [12.15.0] - 2026-10-18

### Added

- `GET /LocalRecs/Worlds/{id}/Items`: all titles of a world the signed-in user can see, including watched ones and titles that belong to the world only via the list (TMDB/IMDb/TVDB IDs, e.g. Bud Spencer & Terence Hill or the Star Trek series). Sorting `year` (newest first, default), `year-asc`, `name`, `rating`; filter `type=movies|series`; paging with `startIndex`/`limit` (max. 500). Former IDs (aliases) work too.

## [12.14.0] - 2026-10-18

### Added

- **fanart.tv logos** for worlds: logo sources `fanart:collection/{tmdbCollectionId}`, `fanart:movie/{tmdbId}`, `fanart:tv/{tvdbId}` (HD before normal, German before English before language-neutral, then likes). New setting `FanartApiKey` (personal key, stays on the server). A world can list several logos in order (`"logo": [ … ]`); the first that can be loaded is used, failed sources are retried after an hour, each source is cached separately.
- **Aliases** for worlds (`aliase` in the list): former IDs keep working in all world URLs, e.g. `harry-potter` after the rename to "Wizarding World".

## [12.13.0] - 2026-10-18

### Added

- **World logos**: field `logo` in the world list (https URL, e.g. a TMDB company logo, or a path relative to the list). `GET /LocalRecs/Worlds` returns `LogoUrl` (`/LocalRecs/Worlds/{id}/Logo`) for worlds with a logo; the endpoint serves the image without sign-in, cached on the server (`welten-logos/`, refreshed weekly, last good image kept on errors) with `Cache-Control: public, max-age=86400`. Only https sources and image files up to 2 MB; local logos must lie inside the folder of a local list.

## [12.12.0] - 2026-10-18

### Changed

- The world list moved to its own repository [Jellyfin-Welten](https://github.com/DrdotHouse2106/Jellyfin-Welten): complete from TMDB (626 titles, also announced ones), updated weekly by a GitHub Action; manual additions and exclusions are kept in `regeln.json` there and are never overwritten. New default for `WorldListSource`; configurations still pointing to the former list in this repository are switched automatically (the cached copy is reused until the first load).

## [12.11.0] - 2026-10-18

### Added

- **Maintained world list** `welten/welten.json` in this repository: per world studios, collection names, single titles (`titel`, by TMDB/IMDb/TVDB ID with optional `typ`) and exclusions (`ausschluss`, by ID or whole collection via `sammlung`). New setting `WorldListSource` (default: the list on GitHub; URL or file; empty = only the text rules). Loaded daily in the background, the last good state is kept in `welten-cache.json` and used when the source is unreachable or invalid. The library is not changed.
- **"Welten" tab** in the settings and admin API (`GET /LocalRecs/Worlds/Admin`, `GET /LocalRecs/Worlds/Admin/{id}`, `POST /LocalRecs/Worlds/Admin/Reload`): titles per world with the reason (studio, collection, list), suggestions for missing titles (other parts of the same collections) and a ready-made line for the list.
- Studio Ghibli added to the default worlds.

### Changed

- Star Wars no longer includes Indiana Jones (same studio Lucasfilm), via exclusion in the list.

## [12.10.1] - 2026-10-18

### Added

- `TmdbCollectionIds` in `GET /LocalRecs/Worlds`: TMDB IDs of the collections belonging to a world (matched by name or containing at least one title of the world), so apps can show missing parts, e.g. via Jellyseerr.

## [12.10.0] - 2026-10-18

### Added

- **Worlds** like on Disney+: movies and series grouped by studio and Jellyfin collection (partial names). Default worlds: Marvel, Star Wars, DC, Pixar, Disney, DreamWorks, Illumination, Harry Potter, Mittelerde, James Bond, Jurassic Park; editable as text (`WorldDefinitions`, one line per world `Name | Studios | Collections`), shown from `MinWorldItems` (default 5) visible titles.
- API for apps: `GET /LocalRecs/Worlds`, `GET /LocalRecs/Worlds/{id}/Recommendations?limit=N`, `GET /LocalRecs/Worlds/Recent?limit=N` (regular token). Titles are ranked per user during the daily refresh (taste profile and rating, next part of a started film series first); watched, started and disliked titles are removed when shown, new titles are appended by rating. Reasons of world titles are added to `/LocalRecs/Recommendations/Reasons`.

## [12.9.0] - 2026-10-18

### Added

- **Film series**: if a part of a Jellyfin collection (BoxSet) was watched, the next unwatched part (by release date) is put at the front of the movie row, at most a quarter of the row; started or disliked parts are skipped, the minimum rating still applies. Setting `EnableCollectionNextParts` (default on).
- **Reasons**: each movie/series recommendation gets the most similar watched title as reason ("Weil du „Frieren“ geschaut hast", similarity ≥ 0.35) or "Nächster Teil nach „…“". Shown above the title on the Home Screen Sections cards (setting `ShowRecommendationReason`, default on) and available for apps via `GET /LocalRecs/Recommendations/Reasons`. `GET /LocalRecs/Recommendations` returns the items unchanged.

### Fixed

- Sporadically failing unit test (test vectors were based on the per-process random `string.GetHashCode()`).

## [12.8.0] - 2026-10-18

### Added

- **Ignored recommendations move back**: titles recommended for more than `IgnoredRecommendationDays` (default 14, 0 = off) without being watched lose 2 % score per day, down to half. The uninterrupted recommendation period is tracked in `feedback.json` (restarts after a gap of 7 days).
- **Abandoned movies as weak dislike**: movies stopped before 25 % of the runtime and not resumed for 3 days pull similar titles down (half as strong as "Gefällt mir nicht"). They do not count as watched items.
- **Genre variety** for movie and series rows: at most a third from the same main genre, filled up with the next best titles (as in "Heute im TV für dich").
- **No repeats of seen episodes** in "Heute im TV für dich": episodes already watched live or in the library (matched by show and episode title) are skipped; the next new episode is shown instead.

## [12.7.1] - 2026-10-17

### Added

- **Credits API for live TV**: `GET /LocalRecs/LiveTv/Program/{programId}/Credits` and `GET /LocalRecs/LiveTv/Credits?ids=a,b,c` (max. 200) return actors with roles, directors, writers, year, country, on-screen episode number and IMDb/TMDb IDs from the XMLTV source. Regular token with live TV access; `Matched: false` (HTTP 200) if the XMLTV file has no data, 404 for unknown program IDs.
- XMLTV source also reads actor roles, `<writer>`, `<episode-num>` (onscreen, xmltv_ns, imdb.com, themoviedb.org).

## [12.7.0] - 2026-10-17

### Added

- **Actors from an XMLTV file** (new setting `LiveTvXmltvSource`, path or http(s) URL, `.xml` or `.xml.gz`): Jellyfin stores no people for live TV programs. The plugin reads actors (max. 10), directors, production year and country from the given XMLTV file and matches them by start time (UTC, minute) and title, using the episode title if several programmes match. Used for the candidates of "Heute im TV für dich" and when recording watched programs. Reloaded in the background when the file changes or every 6 hours; on errors the last good state is kept.
- When the XMLTV source is set, the main actors and directors of watched library movies also feed the live TV interest profile, so movies with favourite actors rank higher in "Heute im TV für dich".
- Status shows the XMLTV source (programmes, with people, last load, error).

## [12.6.0] - 2026-10-17

### Added

- **Thumbs up/down**: "Gefällt mir" (Jellyfin `Likes = true`) counts like a favorite, even for titles not watched yet. "Gefällt mir nicht" (`Likes = false`) is never recommended and pulls similar titles down in the taste profile. Set in the TV app [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) on the detail page.
- **Top 10 API**: `GET /LocalRecs/Top10?type=all|movies|series&days=60&limit=10` returns the most watched movies and series of all users (more different viewers first, then more plays). No admin rights needed; only titles the signed-in user may see, no user names.

## [12.5.0] - 2026-10-16

### Added

- **API for TV apps**: `GET /LocalRecs/Recommendations?type=movies|series|livetv&limit=N` returns the recommendations of the signed-in user (same items as the home screen rows, no admin rights needed, works without Home Screen Sections). Used by the Android TV / Fire TV app [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client).

## [12.4.2] - 2026-10-16

### Added

- **Minimum rating** (`MinCommunityRating`, default 5.0, 0 = off): weakly rated movies and series are no longer recommended; titles without a rating are kept. The row is still filled up with the next best titles.
- **Rating weight** (`RatingQualityWeight`, default 0.15): well rated titles rank higher among similarly matching ones (independent of rating proximity).
- **Live TV diagnostics** in the status: playback events since server start (of which live TV), last time no program was found in the guide, and a list of the recently seen playbacks (user, client, type, title, recognized as live TV). The first detected live TV playback is logged.

### Changed

- TV cards are consistent: line 1 is the title (series with episode, e.g. "The Rookie: Leben und Tod"; sports with the pairing first), line 2 always shows time and channel.

## [12.4.1] - 2026-10-16

### Added

- Button "Auswertung herunterladen (JSON)" in the "Auswertung" tab.

## [12.4.0] - 2026-10-16

### Added

- **Broadcast time on the cards** of "Heute im TV für dich": the second line shows e.g. "Heute 20:15 · ZDF", "Morgen 09:10 · RTL", "Sa 14:00 · Das Erste" or "Läuft bis 15:00 · ARTE" (new setting `LiveTvTimeZone`, default Europe/Berlin). For sports the pairing comes first in the title.
- **Regular shows**: a show watched live on at least `LiveTvRegularDays` (default 3) different days (e.g. "Die Sendung mit der Maus") gets a bonus, is always included and may be up to 7 days ahead.
- **Evaluation**: recommendations and later watching are recorded (`feedback.json`); new tab "Auswertung" and `GET /LocalRecs/Evaluation?days=N` with recommended/watched/hit rate/average days per section and the latest hits.
- Live TV history retention (`LiveTvHistoryDays`) is configurable in the UI up to 3650 days; the recency half-life can be set up to 3650 days.

### Fixed

- **Live TV tracking with clients that do not report progress**: active sessions are now checked every minute in addition to playback events (status shows currently running live TV).
- **Duplicate movies/series** (several versions of the same title) are shown once (provider IDs, otherwise name + year).
- "Heute im TV für dich" is sorted chronologically and limited to a third per main genre (previously 17 of 20 were comedies, sorted in blocks).
- **Recalculation after plugin updates**: on startup recommendations are recomputed if they were created by another plugin version or are older than 24 hours.

## [12.3.3] - 2026-10-16

### Fixed

- **Empty home screen with Home Screen Sections**: the section IDs were GUIDs starting with a digit. Home Screen Sections uses the ID as CSS class name (`querySelector('.' + id + '[data-page="1"]')`), which threw a `SyntaxError` in the browser and aborted the whole home screen. The sections now use the IDs `LocalRecsMovies`, `LocalRecsShows` and `LocalRecsLiveTv`.
- **Migration**: on startup the old IDs are replaced in the Home Screen Sections admin settings (`SectionSettings`) and in the per-user settings (`ModularHomeSettings.json`, backup `ModularHomeSettings.json.bak-localrecs`), so nothing has to be re-enabled.

### Known

- Home Screen Sections still logs "No translation found for key 'LocalRecs…'" because it looks up the section ID in its own translation packs; the German titles from the plugin settings are used as fallback. The warning is harmless.

## [12.3.2] - 2026-10-16

### Changed

- **Sport detection** for "Heute im TV für dich" based on real EPG data: the sport is detected from genres first ("Fußball", "1. Bundesliga", "Handball", …), then from title/episode title if the program is a sports program or has no genre ("Bundesliga 26/27", "Bundesliga: …", "Fußball: Champions League"). Sports programs are recommended if the sport matches what the user watched, so football fans get football from all providers (Sky, DAZN, Sportdigital) but no handball, golf or tennis.
- Several sports broadcasts with the same title (e.g. multiple Champions League games) are shown individually (deduplicated by title + pairing instead of title only).
- Short abbreviations (NFL, ATP, …) only match whole words; titles of non-sports programs are not used for sport detection (e.g. "Tatort: Bundesliga-Mord").

## [12.3.1] - 2026-10-16

### Fixed

- "Heute im TV für dich": sports programs are only recommended if their title matches a watched title (e.g. "Fußball"). Previously the shared genre "Sport" made handball, golf or tennis show up for football fans.

## [12.3.0] - 2026-10-16

### Added

- **Live TV tracking**: playback reports of all clients (including native TV apps) are evaluated; for live TV the running program is looked up in the EPG and watched minutes are counted per broadcast. Metadata (title, series, genres, people, year) is copied at watch time because Jellyfin deletes old EPG entries. Stored in `plugins/LocalRecs/livetv-history.json`, saved every 5 minutes, kept 180 days. Zapping below `LiveTvMinMinutes` (default 3) and `LiveTvExcludedGenres` (default "Nachrichten, News, Wetter") are ignored. Channel switching (e.g. football on several channels) is counted per broadcast and adds up via title/genre.
- **"Heute im TV für dich"**: third home screen section with upcoming programs (next `LiveTvLookaheadHours`, default 24) ranked by an interest profile built from live TV minutes and library history (watched movies, series with watched episodes). Each show appears once (next airing); already watched broadcasts are skipped.
- Titles watched live for at least `LiveTvTitleMatchMinutes` (default 15) that also exist in the library count as watched for the library recommendations.
- New settings in the "Startseite" tab; status shows the number of tracked broadcasts.

## [12.2.0] - 2026-10-16

### Changed (breaking)

- **Recommendations are shown as home screen rows instead of per-user libraries.** The plugin registers the sections "Empfohlene Filme für dich" and "Empfohlene Serien für dich" with the [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) plugin. The sections return the real library items for the requesting user: full metadata, shared watch state, no duplicates in "Continue Watching", no library scan and no extra libraries. Requires Home Screen Sections, File Transformation and Plugin Pages; native TV apps do not show the rows.
- Recommendations are stored per user in `plugins/LocalRecs/recommendations.json` and survive restarts. Watched or started items are filtered out at request time. The first calculation starts automatically after installation.
- Recommendation libraries and symlink folders created by older versions are removed automatically on startup (media files are untouched), and their IDs are removed from users' library access lists.
- Removed: symlink/virtual library management, play status sync, automatic library creation and access management (including the settings `AutoCreateLibraries`, `AutoManageLibraryAccess`, `MovieLibraryNameTemplate`, `TvLibraryNameTemplate`) and the endpoints `Setup/Paths` and `Setup/Provision`. New: `MovieSectionTitle`, `TvSectionTitle` and `GET /LocalRecs/Status`.
- The "Setup" tab of the configuration page is now "Startseite" with connection status and row titles.
- The Windows symlink requirement no longer applies.

## [12.1.1] - 2026-10-16

### Changed

- Libraries created after the access switch are **no longer granted automatically** to users that previously had "access to all libraries". The admin grants new libraries per user (useful for libraries meant for a single user). The internal settings `ManagedAllLibrariesUserIds` and `KnownLibraryIds` were removed.

## [12.1.0] - 2026-10-16

### Added

- **Automatic library setup**: the plugin now creates the two recommendation libraries (movies and TV) for every user by itself – on server start, when a user is created, before every recommendation refresh, and via the new "Speichern und jetzt einrichten" button (`POST /LocalRecs/Setup/Provision`). Existing libraries are detected by path and reused. When a user is deleted, their recommendation libraries are removed.
- **Automatic library access**: every user only sees their own recommendation libraries. Users with "access to all libraries" are switched to an explicit list of all regular libraries plus their own recommendations. Already restricted users keep their selection.
- New settings in the Setup tab: `AutoCreateLibraries`, `AutoManageLibraryAccess`, `MovieLibraryNameTemplate`, `TvLibraryNameTemplate` (defaults: "Empfohlene Filme – {user}", "Empfohlene Serien – {user}").

### Changed

- The Setup tab of the configuration page is now in German and shows a per-user status table instead of manual instructions (manual paths are still shown when automatic creation is disabled).
- New recommendation libraries are created with chapter and trickplay image extraction disabled, because their files are only symlinks to the real library.

## [12.0.0] - 2026-10-16

### Changed

- **Jellyfin 12 compatibility**: the plugin now targets .NET 10 and the Jellyfin 12.0.0 packages (`targetAbi` `12.0.0.0`). Verified on a Jellyfin 12.2 server: plugin loads, setup API and configuration page work, refresh and benchmark tasks complete and recommendation symlinks are created.
- **Version scheme aligned with Jellyfin**: this fork is versioned 12.x (previously 0.x upstream). Jellyfin treats `targetAbi` only as a *minimum* server version, so 10.11-targeted upstream releases would otherwise be offered as updates on Jellyfin 12 when both repositories are configured. A 12.x version number always ranks above upstream releases. The plugin ID is unchanged, so an installed upstream version upgrades in place.
- `manifest.json` only lists Jellyfin-12 releases; the old 10.11 versions (0.2–0.6.1) were removed from the catalog.
- CI now uses the .NET 10 SDK.

### Internal

- `RecommendationEngineTests` use dictionary-backed mocks for `GetItemById`/`GetUserData` instead of one Moq setup per item (the 2000-item performance test took >20 s because Moq scans setups linearly).

### Upgrade Notes

- **Requires Jellyfin 12.0 or newer.** Servers on Jellyfin 10.11.x should stay on 0.6.1.

## [0.6.1] - 2026-05-30

### Fixed

- **Jellyfin 10.11.9+ compatibility (#17, fixes #21)**: the recommendation refresh, benchmark, setup, and play-status sync tasks no longer crash with `MissingMethodException: IUserManager.get_Users()`. Jellyfin 10.11.9 removed the `IUserManager.Users` property as part of its EFCore refactor ([jellyfin#15368](https://github.com/jellyfin/jellyfin/pull/15368)); all six call sites now use `GetUsers()`. **This release requires Jellyfin 10.11.9 or newer** (see Upgrade Notes).
- **TV series recency (#19)**: a series is now weighted by the date of its most recently watched episode. Previously every watched series was treated as watched *today* — Jellyfin never sets `LastPlayedDate` on the series item itself — which inflated TV influence on the taste profile regardless of when it was actually watched.
- **Config page numeric fields (#22)**: numeric settings saved as `0` no longer silently revert to their hardcoded defaults on the next page load (JavaScript `||` treated `0` as falsy).

### Changed

- **Recent-watch emphasis replaces rewatch boost (#20)**: the play-count–based rewatch boost is replaced by a recency-driven `decay²` amplification (`weight = decay × (1 + RecentWatchBoost × decay)`). Jellyfin's `PlayCount` increments on every stop/start of a stream and is near-useless at the series level, so recency is now used as the preference-strength proxy (a genuine re-watch resets recency anyway). The `RewatchBoost` setting (default 1.5) is replaced by **`RecentWatchBoost`** (default 1.0); the old value is dropped on first save. Set `RecentWatchBoost = 0` to disable the emphasis entirely.

### Internal

- Removed the now-unused `WatchRecord.PlayCount` field (#25) and added regression tests covering the TV-series recency path (#24).

### Upgrade Notes

- **Requires Jellyfin 10.11.9.0 or newer.** This release targets ABI `10.11.9.0` and is **not** compatible with 10.11.0–10.11.8 (the `GetUsers()` API does not exist there). Jellyfin's plugin catalogue will only offer this update to servers running 10.11.9+; servers on older 10.11.x builds should remain on 0.6.0 until they update Jellyfin.
- Recommendation ranking will shift on upgrade: the move to recency-based emphasis (and the series-recency fix) changes how watch history is weighted. Recommendations regenerate on the next scheduled refresh.

### Credits

- Huge thanks to [@PascalGodin](https://github.com/PascalGodin), who contributed the bulk of this release — the 10.11.9 compatibility fix (#17), the TV-series recency fix (#19), the recent-watch emphasis redesign (#20), and the config-page zero-value fix (#22).

## [0.6.0] - 2026-04-15

### Changed

- **Virtual libraries now use filesystem symlinks instead of `.strm` files (#13)**. Fixes transcoded playback on Jellyfin 10.11.7+, which silently rejects local paths in `.strm` files as part of security advisory [GHSA-j2hf-x4q5-47j3](https://github.com/jellyfin/jellyfin/security/advisories/GHSA-j2hf-x4q5-47j3). Symlinks have the source file's real extension, so Jellyfin's media pipeline treats them as regular media — transcoding, probing, and artwork discovery all work natively.
- **Artwork is now symlinked from the source folder** (`poster.jpg`, `fanart.jpg`, etc.) instead of being copied. Custom artwork on source items propagates automatically.
- **Trailer discovery delegated to Jellyfin.** Plugin symlinks trailer files by name; Jellyfin's scanner handles the rest.

### Removed

- **`ImageSyncService`** and its associated configuration (`EnableImageSync`, `SyncBackdrops`). Symlinked artwork supersedes the copy-based approach.
- **Custom trailer scanning logic** (~65 lines) and the video-extension heuristic.

### Fixed

- **`tvshow.nfo` written for series folders** so Jellyfin's scanner reliably identifies them as Series instead of rendering individual episodes as standalone items.
- **Series poster rendering**: artwork now resolved via `BaseItem.GetImagePath` (which works for metadata-cache storage) rather than scanning the source folder.
- **Additional artwork aliases** (`folder.jpg`, `backdrop.jpg`) symlinked alongside `poster.jpg`/`fanart.jpg` to suppress Jellyfin core warnings for conventional filenames it probes.
- **Reduced log noise**: per-user/per-item progress messages demoted from INFO to DEBUG. Refresh-level summary lines remain at INFO.

### Upgrade Notes

- **Linux / Docker-on-Linux:** No action required. Virtual libraries regenerate on the next scheduled refresh.
- **Windows hosts:** Jellyfin must run as Administrator **or** Windows Developer Mode must be enabled (Settings → Privacy & security → For developers). Without one of these, the plugin logs `Access denied creating symlink` and the virtual libraries remain empty. See README Troubleshooting section.
- Existing `.strm`-based virtual libraries are cleared and rebuilt on the next recommendation refresh — no manual migration needed.

## [0.5.3] - 2026-03-23

### Fixed

- **User Library Access Filtering (#10)**: Recommendations now respect per-user library access. Items from libraries a user cannot access (including disabled libraries) are excluded from both personalized and cold-start recommendation paths.

## [0.5.2] - 2026-02-08

### Fixed

- **Playback Freeze from Recommendations (#8)**: Playing items from recommendation libraries no longer freezes playback. The plugin was causing a storm of database writes every ~10 seconds during active playback by syncing every position update to the source library.

### Changed

- **Deferred Removal**: Virtual library items are never removed from event handlers. Watched items remain in recommendation libraries until the next scheduled refresh cleans them up naturally.
- **SaveReason Filtering**: Only meaningful events (PlaybackFinished, TogglePlayed, UpdateUserRating) trigger play status sync. PlaybackStart and PlaybackProgress events are ignored entirely.
- **Code Cleanup**: Removed dead removal code (RemoveVirtualLibraryItem, FindSeriesFolderForItem, TriggerLibraryScan, active session tracking).

### Known Issues

- Partially watched recommendations may appear twice in "Continue Watching" / "Next Up" (once for the .strm item, once for the source). Resolves on next recommendation refresh.

## [0.4.0] - 2025-12-28

### Added

- **Decade-Based Temporal Similarity**: Recommendations now consider content from similar time periods using categorical decade grouping (1980s, 1990s, etc.) instead of continuous year normalization
  - Improves temporal relevance alongside existing genre/actor/director similarity features
  - Tested with production data: ~12 decades extracted from 970 items
  - Observable impact: 24% of movie recommendations changed, 8% of TV recommendations changed

### Fixed

- **In-Progress Series Filtering**: TV series with unwatched episodes no longer appear in recommendations (prevents recommending shows you're currently watching)

## [0.3.0] - 2025-12-27

### Fixed

- **Series Filtering**: Fully watched series no longer appear in recommendations. Previously relied on unreliable `userData.Played` flag; now queries for unwatched episodes directly
- **Play Status Sync**: Virtual library items now correctly reflect source library watch status when scanned by Jellyfin

### Added

- **Play Status Sync on Item Add**: When Jellyfin scans new virtual library items, their play status is automatically synced from the source library via `ItemAdded` event
- **Play Status Sync on Startup**: Existing virtual library items sync play status from source library when plugin initializes
- **Rating Proximity Scoring**: Optional feature to boost recommendations with similar community/critic ratings to user's watched content

### Changed

- Refactored `PlayStatusSyncService` to reduce code duplication with extracted helper methods
- Reduced debug logging noise in production for cleaner logs
- Removed ineffective sync call from recommendation refresh task (items aren't indexed yet when it runs)

### Removed

- **NFO File Generation**: Removed NFO metadata files as Jellyfin doesn't read NFO files for .strm content (metadata comes from the source library item)

## [0.2.1] - 2025-12-26

### Fixed

- **NFO Encoding**: Fixed XML encoding from UTF-16 to UTF-8 so Jellyfin properly reads metadata (runtime, etc.)
- **Cast & Crew**: NFO files now include actors, directors, and writers from source media
- **Stream Details**: NFO files now include video/audio/subtitle stream information for proper stream selector display

### Added

- **FileInfo Section**: NFO files now contain `<fileinfo><streamdetails>` with codec, bitrate, resolution, framerate, language, and channel information

## [0.2.0] - 2025-12-26

### Added

- **NFO Metadata Support**: Virtual library items now include NFO files with full metadata (runtime, ratings, genres, studios, tags, provider IDs)
- **Local Trailer Support**: Trailers from source media are now linked in virtual libraries using `-trailer.strm` files
- **Movie Folder Structure**: Movies now use proper folder structure with NFO files for better metadata support

### Fixed

- Copy buttons on setup page now work with fallback clipboard support for broader browser compatibility
- Manifest now correctly references ZIP file instead of raw DLL

### Changed

- Improved README with detailed installation instructions and algorithm documentation
- Simplified bug report template

## [0.1.0] - 2025-12-26

### Initial Beta Release

Privacy-first personalized recommendations for Jellyfin based on local watch history.

#### Features

- **Per-User Personalization**: Each user receives recommendations tailored to their viewing history
- **Content-Based Filtering**: Uses TF-IDF embeddings and cosine similarity to find similar content
- **Virtual Library Integration**: Recommendations appear as dedicated libraries accessible from all Jellyfin clients (web, mobile, Roku, etc.)
- **Privacy-First Design**: All processing happens locally on your server with zero external dependencies or tracking
- **Configurable Weighting**:
  - Favorite boost (default 2.0x)
  - Rewatch boost (default 1.5x)
  - Recency decay with configurable half-life (default 365 days)
- **Smart Filtering**:
  - Abandoned series exclusion (configurable threshold, default 90 days)
  - Minimum watch history requirement (default 3 items)
  - Excludes already-watched content
- **Flexible Updates**:
  - Daily scheduled task (configurable time)
  - Manual refresh available anytime
- **Performance Optimized**: Handles libraries of 2,000+ items efficiently with vocabulary limiting and parallel processing

#### Technical Details

- **Target**: Jellyfin Server 10.11.5+
- **Runtime**: .NET 9.0
- **Target ABI**: 10.11.0.0
- **Architecture**: Content-based filtering with TF-IDF, cosine similarity, and weighted user profiles
- **Storage**: Per-user .strm files in plugin data directory

#### Supported Metadata

- Genres
- Actors (top 500 by frequency)
- Directors
- Tags (top 500 by frequency)
- Content Ratings
- Release Years

#### Known Limitations

- Requires manual one-time library setup per user (5-10 minutes)
- Cold start: Users with fewer than 3 watched items receive popular content recommendations
- No collaborative filtering (recommendations based solely on individual user's history)
- Series recommendations based on series-level metadata only (not individual episodes)

[0.4.0]: https://github.com/rdpharr/jellyfin-plugin-localrecs/releases/tag/v0.4.0
[0.3.0]: https://github.com/rdpharr/jellyfin-plugin-localrecs/releases/tag/v0.3.0
[0.2.1]: https://github.com/rdpharr/jellyfin-plugin-localrecs/releases/tag/v0.2.1
[0.2.0]: https://github.com/rdpharr/jellyfin-plugin-localrecs/releases/tag/v0.2.0
[0.1.0]: https://github.com/rdpharr/jellyfin-plugin-localrecs/releases/tag/v0.1.0
