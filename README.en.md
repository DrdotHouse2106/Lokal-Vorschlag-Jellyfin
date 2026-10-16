# Jellyfin Local Recommendations Plug-in

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

Privacy-first personalized recommendations for Jellyfin based entirely on local watch history and metadata similarity. No cloud services, no tracking. Works on all Jellyfin clients (even TVs).

> [!NOTE]
> **Based on / original:** This plugin is based on the original project **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** by Roger Pharr.
> This repository is a version adapted for **Jellyfin 12**, as the original will not be updated for Jellyfin 12. All credit for the actual development goes to the original author.
>
> German version: [README.md](README.md)

Please report any issues or feedback on [GitHub Issues](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/issues).

> ⚠️ **Windows hosts:** As of v0.6.0, this plugin creates filesystem symlinks to expose recommendations. On Windows, symlink creation requires either **running Jellyfin as Administrator** or **enabling Windows Developer Mode** (Settings → Privacy & security → For developers → Developer Mode). Without one of these, recommendation refreshes will log "Access denied creating symlink" and the virtual libraries will be empty. Docker-on-Linux and native Linux deployments are unaffected. See [Troubleshooting](#troubleshooting) below.

## Features

- **Per-user personalization** - Tailored recommendations for each user's viewing history
- **Content-based filtering** - TF-IDF embeddings with cosine similarity matching
- **Temporal similarity** - Decade-based grouping finds content from similar time periods
- **Virtual library integration** - Works on all Jellyfin clients (web, mobile, Roku, etc.)
- **Play status sync** - Watch state syncs from recommendations back to your real library
- **Privacy-first** - All processing happens locally on your server
- **Performance optimized** - Handles 2,000+ item libraries efficiently

## Requirements

- **Jellyfin Server:** 12.0+ (tested on 12.2)
- **.NET Runtime:** 10.0
- **Target ABI:** 12.0.0.0

For Jellyfin 10.11.x use upstream version 0.6.1.

## Installation

1. **Add plugin repository:**  
   Dashboard → Plugins → Repositories → Add  
   `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json`

2. **Install plugin:**  
   Dashboard → Plugins → Catalog → Install "Local Recommendations"

3. **Restart Jellyfin server**

4. **Configure virtual libraries** (see Setup below)

## Setup

Setup is **automatic** (since 12.1.0):

- For **every user** the plugin creates two libraries ("Empfohlene Filme – *name*" and "Empfohlene Serien – *name*") on server start, when a user is created, and before every recommendation refresh. Existing recommendation libraries are detected by path and reused.
- **Library access:** every user only sees their own recommendation libraries. Users with "access to all libraries" are switched to an explicit list of all regular libraries plus their own recommendations; libraries added later are granted to them automatically. If an admin later removes a library from such a user, the user is treated as deliberately restricted and no longer receives new libraries automatically. Users that were already restricted keep their selection.
- When a user is deleted, their recommendation libraries are removed (media files are untouched).
- Settings: **Dashboard → Plugins → Local Recommendations → Setup** – toggle automatic creation and access management, change the library name templates (`{user}` = username) and run the setup immediately.

Generate recommendations: Dashboard → Scheduled Tasks → "Refresh Local Recommendations" → Run Now (runs daily at 4:00 AM by default). New recommendations appear after the next library scan.

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

### Virtual Libraries

Recommendations appear as separate libraries for each user:

- Plugin creates filesystem symlinks pointing to original media files (with matching artwork symlinks)
- The plugin creates the Jellyfin libraries pointing to these directories and manages library access automatically
- Each user gets Movies and TV libraries with personalized recommendations
- Play status sync: Watch state on recommendation items is synced back to the source library
- Watched items are cleaned up at the next scheduled recommendation refresh

### Privacy

**100% local processing:**
- No external services or cloud dependencies
- No tracking or telemetry
- Only uses data already in your Jellyfin database
- All computation happens on your server

## Known Limitations

- **Duplicate "Continue Watching" / "Next Up":** Partially watched recommendations appear twice — once for the virtual (symlinked) item and once for the source media file. This resolves on the next recommendation refresh, or you can manually trigger a refresh from Scheduled Tasks.
- **Metadata display:** Virtual library items may not show full text metadata (runtime, ratings, cast) in the UI because Jellyfin treats them as items in a separate library. Posters, backdrops, and playback work normally.
- **Library scanning:** Manually scan recommendation libraries after refresh to see updates
- **Windows symlink permissions:** Requires Administrator or Developer Mode — see Troubleshooting.

## Troubleshooting

### Recommendation libraries are empty after refresh (Windows)

If the refresh task completes without errors but the recommendation libraries are empty, check
the Jellyfin log for `Access denied creating symlink`. This means the Jellyfin process lacks
permission to create symbolic links, which is required on Windows.

**Fix (pick one):**

1. **Enable Windows Developer Mode** (recommended, no elevation needed):
   Settings → Privacy & security → For developers → turn on **Developer Mode**, then restart
   the Jellyfin service.
2. **Run Jellyfin as Administrator.** Right-click the service or launcher → Run as administrator.
   If Jellyfin runs as a Windows service, configure the service's logon account to a user with
   `SeCreateSymbolicLinkPrivilege` granted, or to a local administrator.

After either change, run **Dashboard → Scheduled Tasks → Refresh Local Recommendations**.

### Transcoded playback fails on Jellyfin 10.11.7+ (plugin versions ≤0.5.3)

Upgrade to **v0.6.0 or later**. Jellyfin 10.11.7 shipped a security fix
([GHSA-j2hf-x4q5-47j3](https://github.com/jellyfin/jellyfin/security/advisories/GHSA-j2hf-x4q5-47j3))
that broke the old `.strm`-based approach. v0.6.0 switches to symlinks, which bypass the
restricted `.strm` parser entirely.

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

## License

GNU General Public License v3.0 - see [LICENSE.txt](LICENSE.txt)
