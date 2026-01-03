# Jellyfin Local Recommendations Plug-in

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/rdpharr/jellyfin-plugin-localrecs/actions/workflows/tests.yml/badge.svg)](https://github.com/rdpharr/jellyfin-plugin-localrecs/actions/workflows/tests.yml)

Privacy-first personalized recommendations for Jellyfin based entirely on local watch history and metadata similarity. No cloud services, no tracking. Works on all Jellyfin clients (even TVs).

Please report any issues or feedback on [GitHub Issues](https://github.com/rdpharr/jellyfin-plugin-localrecs/issues).

## Features

- **Per-user personalization** - Tailored recommendations for each user's viewing history
- **Content-based filtering** - TF-IDF embeddings with cosine similarity matching
- **Temporal similarity** - Decade-based grouping finds content from similar time periods
- **Virtual library integration** - Works on all Jellyfin clients (web, mobile, Roku, etc.)
- **Auto-removal** - Recommendations disappear when you start watching them
- **Privacy-first** - All processing happens locally on your server
- **Performance optimized** - Handles 2,000+ item libraries efficiently

## Requirements

- **Jellyfin Server:** 10.11.5+
- **.NET Runtime:** 9.0
- **Target ABI:** 10.11.0.0

## Installation

1. **Add plugin repository:**  
   Dashboard → Plugins → Repositories → Add  
   `https://raw.githubusercontent.com/rdpharr/jellyfin-plugin-localrecs/main/manifest.json`

2. **Install plugin:**  
   Dashboard → Plugins → Catalog → Install "Local Recommendations"

3. **Restart Jellyfin server**

4. **Configure virtual libraries** (see Setup below)

## Setup

### Quick Start (5-10 minutes)

#### 1. View Library Paths
- Navigate to: **Dashboard → Plugins → Local Recommendations → Setup Guide**
- Copy the library paths for each user (two per user: Movies and TV)

#### 2. Create Virtual Libraries
For each user, create **two** libraries:

**Movies:**
- Dashboard → Libraries → Add Media Library
- Content Type: **Movies**
- Add media location: Paste the **Movie Library Path** from Setup Guide
- Library name: User's suggested name (e.g., "John's Recommended Movies")

**TV Shows:**
- Content Type: **Shows**
- Add media location: Paste the **TV Library Path** from Setup Guide
- Library name: User's suggested name (e.g., "John's Recommended TV")

#### 3. Set Permissions
For each user:
- Dashboard → Users → [Username] → Library Access
- Enable **only** that user's recommendation libraries
- Disable other users' recommendation libraries

#### 4. Generate Recommendations
- Dashboard → Scheduled Tasks → "Refresh Local Recommendations" → Run Now
- Wait ~1-5 minutes (depending on library size)
- Manually scan recommendation libraries to see results

## Configuration

Access via: **Dashboard → Plugins → Local Recommendations → Settings**

### Key Settings

**Recommendation Counts**
- Movies and TV shows to recommend per user (default: 25 each)
- Minimum watched items for personalization (default: 3)

**Filtering**
- Exclude abandoned TV series (default: enabled, 90 days threshold)
- Auto-remove recommendations when user starts watching them

**Weighting Factors**
- Favorite boost: 2.0x (configurable)
- Rewatch boost: 1.5x (configurable)
- Recency decay half-life: 365 days (configurable)

**Optional Features**
- Rating proximity scoring (boost items with similar ratings)
- Decade-based temporal similarity (finds content from similar eras)

**Performance**
- Vocabulary limits for actors/tags (default: 500 each)
- Parallel processing options

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
- Rewatches (1.5x boost)
- Recency decay (365-day half-life)
- Decade similarity (items from similar time periods)
- Optional rating proximity (items with similar ratings)

### Virtual Libraries

Recommendations appear as separate libraries for each user:

- Plugin creates `.strm` files pointing to original media files
- Admin creates Jellyfin libraries pointing to plugin directories (one-time setup)
- Each user gets Movies and TV libraries with personalized recommendations
- Auto-removal: Recommendations disappear when playback starts (prevents duplicate "Continue Watching" entries)

### Privacy

**100% local processing:**
- No external services or cloud dependencies
- No tracking or telemetry
- Only uses data already in your Jellyfin database
- All computation happens on your server

## Known Limitations

- **Metadata display:** Virtual library items may not show full metadata (runtime, ratings, cast) in the UI due to Jellyfin's `.strm` file handling. Playback and posters work normally.
- **Manual setup required:** Admin must manually create libraries and set permissions (Jellyfin API limitation)
- **Library scanning:** Manually scan recommendation libraries after refresh to see updates

## Building from Source

**Prerequisites:** .NET 9.0 SDK, Git

```bash
git clone https://github.com/rdpharr/jellyfin-plugin-localrecs.git
cd jellyfin-plugin-localrecs

# Build (uses dotnet-helper.sh wrapper)
bash dotnet-helper.sh build

# Run tests
bash dotnet-helper.sh test

# Output: Jellyfin.Plugin.LocalRecs/bin/Debug/net9.0/
```

**Windows:** Use Git Bash or WSL to run the helper script.

## Contributing

Contributions welcome! See [DESIGN.md](DESIGN.md) for technical details and architecture.

## Support

- **Issues:** [GitHub Issues](https://github.com/rdpharr/jellyfin-plugin-localrecs/issues)
- **Discussions:** [GitHub Discussions](https://github.com/rdpharr/jellyfin-plugin-localrecs/discussions)
- **Documentation:** [DESIGN.md](DESIGN.md)

## License

GNU General Public License v3.0 - see [LICENSE.txt](LICENSE.txt)
