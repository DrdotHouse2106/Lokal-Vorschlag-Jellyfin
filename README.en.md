# Jellyfin Local Recommendations Plug-in

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

Privacy-first personalized recommendations for Jellyfin based entirely on local watch history and metadata similarity. No cloud services, no tracking. Recommendations appear as rows on the home screen.

> [!NOTE]
> **Based on / original:** This plugin is based on the original project **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** by Roger Pharr.
> This repository is a version adapted for **Jellyfin 12**, as the original will not be updated for Jellyfin 12. All credit for the actual development goes to the original author.
>
> German version: [README.md](README.md)

Please report any issues or feedback on [GitHub Issues](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/issues).

> [!IMPORTANT]
> Since **12.2.0** recommendations are shown as **rows on the home screen** (via the "Home Screen Sections" plugin) instead of per-user libraries. Recommendation libraries created by older versions are removed automatically on first start. **Native TV apps** (Android TV, Fire TV, Roku) do not show these rows.

## Features

- **Per-user personalization** - Tailored recommendations for each user's viewing history
- **Content-based filtering** - TF-IDF embeddings with cosine similarity matching
- **Temporal similarity** - Decade-based grouping finds content from similar time periods
- **Home screen rows** - "Empfohlene Filme für dich" / "Empfohlene Serien für dich", individual per user
- **Real library items** - full metadata, shared watch state, no duplicates in "Continue Watching", no extra libraries
- **Privacy-first** - All processing happens locally on your server
- **Performance optimized** - Handles 2,000+ item libraries efficiently

## Requirements

- **Jellyfin Server:** 12.0+ (tested on 12.2)
- **.NET Runtime:** 10.0
- **Target ABI:** 12.0.0.0
- **For the home screen rows:** the plugins **Home Screen Sections**, **File Transformation** and **Plugin Pages** by IAmParadox27 (repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`)
- **Clients:** web, desktop app and mobile apps. Native TV apps (Android TV, Fire TV, Roku) do not show the rows.

For Jellyfin 10.11.x use upstream version 0.6.1.

## Installation

1. **Add plugin repository:**  
   Dashboard → Plugins → Repositories → Add  
   `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json`

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

### Privacy

**100% local processing:**
- No external services or cloud dependencies
- No tracking or telemetry
- Only uses data already in your Jellyfin database
- All computation happens on your server

## Known Limitations

- **Native TV apps:** Android TV, Fire TV and Roku apps do not render Home Screen Sections, so recommendations are not visible there.
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

## License

GNU General Public License v3.0 - see [LICENSE.txt](LICENSE.txt)
