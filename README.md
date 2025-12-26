# Local Recommendations - Jellyfin Plugin

[![License: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
![Status: Beta](https://img.shields.io/badge/Status-Beta-yellow.svg)

Privacy-conscious content recommendations for Jellyfin - personalized suggestions based entirely on your local watch history, with zero tracking or cloud dependencies.

## Overview

**⚠️ Beta Release:** This plugin is currently in beta testing. Please report any issues or feedback on GitHub.

Local Recommendations is a Jellyfin server plugin that generates personalized movie and TV show recommendations for each user using only your local watch history and metadata. No cloud services, no tracking, complete privacy.

**Key Features:**
- **Per-user personalized recommendations** - Each user gets recommendations tailored to their viewing history
- **Content-based filtering** - Uses TF-IDF embeddings and cosine similarity to find similar content
- **Simple update model** - Daily scheduled updates (manual refresh anytime via Scheduled Tasks)
- **Privacy-first** - All processing happens locally on your server
- **Works on all clients** - Recommendations appear as virtual libraries accessible from web, Roku, mobile, etc.
- **Performance optimized** - Handles libraries of 2,000+ items efficiently

## Requirements

- **Jellyfin Server:** 10.11.5 or later
- **.NET Runtime:** 9.0
- **Target ABI:** 10.11.0.0

## Installation

*(Installation instructions will be added after initial release)*

1. Add the plugin repository to Jellyfin
2. Install "Local Recommendations" from the plugin catalog
3. Restart Jellyfin server
4. Configure the plugin and set up per-user virtual libraries

## How It Works

### Algorithm

The plugin uses a content-based filtering approach:

1. **Feature Extraction** - Extracts genres, actors, directors, tags, ratings, and release year from each item
2. **TF-IDF Embeddings** - Computes term frequency-inverse document frequency vectors for categorical features
3. **User Profiles** - Builds a taste vector for each user by aggregating weighted embeddings of watched items
4. **Similarity Scoring** - Recommends unwatched items with highest cosine similarity to user's taste vector
5. **Proximity Bonuses** - Optional bonuses for items with similar release years and ratings to user's preferences
6. **Weighting Factors** - Applies boosts for favorites, rewatches, and recency to improve recommendations

### Virtual Libraries

Recommendations are exposed as per-user virtual libraries that appear in Jellyfin's library list. The plugin:

1. **Creates directories** in the plugin data folder for each user (e.g., `{PluginData}/virtual-libraries/{userId}/movies`)
2. **Generates .strm files** that point to the original media files in your library
3. **Admin creates libraries** via Jellyfin UI pointing to these directories (one-time setup)
4. **Sets permissions** so each user sees only their own recommendation libraries

Each user gets:
- A "Recommended Movies" library
- A "Recommended TV" library

The `.strm` files allow Jellyfin to stream the actual media while keeping recommendations separate from your main library.

## Setup

### One-Time Configuration (5-10 minutes total)

#### Step 1: Configure Plugin Settings
1. Navigate to **Dashboard → Plugins → Local Recommendations**
2. Go to the **Settings** tab
3. Configure recommendation settings (counts, weights, etc.)
4. Save configuration

#### Step 2: View Library Paths
1. Go back to the **Setup Guide** tab on the plugin configuration page
2. You'll see paths for each user's libraries with copy buttons
3. Note the suggested library names for each user

#### Step 3: Create Libraries in Jellyfin

**For each user**, create TWO libraries:

1. **Movies Library**:
   - Dashboard → Libraries → Add Media Library
   - Content Type: **Movies**
   - Click "Add media location"
   - Paste the **Movie Library Path** from the plugin page
   - Library Name: Use suggested name (e.g., "John's Recommended Movies")
   - Click OK

2. **TV Library**:
   - Repeat with Content Type: **Shows**
   - Use the **TV Library Path** from the plugin page
   - Library Name: Use suggested name (e.g., "John's Recommended TV")

**Time:** ~2 minutes per user

#### Step 4: Set Library Permissions

For each user:
1. Dashboard → Users → [Username] → Library Access
2. **Enable** ONLY that user's recommendation libraries
3. **Disable** other users' recommendation libraries
4. Save

This ensures users only see their own recommendations.

#### Step 5: Generate Initial Recommendations
1. Dashboard → Scheduled Tasks
2. Find "Refresh Local Recommendations"
3. Click **Run Now** (▶️)
4. Wait for completion (~1-5 minutes depending on library size)

### Configuration Options

Access the plugin configuration page in Jellyfin Dashboard → Plugins → Local Recommendations → Settings tab.

#### Recommendation Settings
- **Movie/TV Recommendation Count** - How many items to recommend (default: 25 each)
- **Min Watched Items** - Minimum watch history before personalization kicks in (default: 3)
- **Exclude Abandoned Series** - Don't recommend partially watched series that haven't been watched recently (default: enabled)
- **Abandoned Series Threshold** - Days of inactivity before a series is considered abandoned (default: 90 days)

#### Update Schedule
Recommendations update automatically via scheduled task:
- **Default:** Daily at 4:00 AM (configurable in Dashboard → Scheduled Tasks)
- **Custom schedule:** Modify time or frequency in Scheduled Tasks UI
- **Manual refresh:** Available anytime via Scheduled Tasks

#### Weighting Factors
- **Favorite Boost** - Multiplier for favorited items (default: 2.0x)
- **Rewatch Boost** - Multiplier for rewatched items (default: 1.5x)
- **Recency Decay Half-Life** - Days for watch weight to decay by half (default: 365)

#### Release Year Preference
- Option to boost recommendations from user's preferred era
- Calculates weighted average of watched content release years
- Adjustable weight for year proximity bonus

#### Rating Preference
- Option to boost recommendations with similar ratings to user's preferences
- Calculates weighted average of watched content ratings
- Adjustable weight for rating proximity bonus

#### Performance Tuning
- Limit vocabulary sizes for actors, directors, and tags (default: 500 for actors and tags)
- Enable/disable parallel processing
- Adjust max parallel tasks

## Privacy

All data processing happens locally on your Jellyfin server. The plugin:
- **Never** sends data to external services
- **Never** tracks or collects user behavior
- **Only** uses data already available in your Jellyfin database

## Building from Source

### Prerequisites
- .NET 9.0 SDK
- Git

### Build Steps

```bash
# Clone the repository
git clone https://github.com/rdpharr/jellyfin-plugin-localrecs.git
cd jellyfin-plugin-localrecs

# Build the plugin (uses dotnet-helper.sh wrapper)
bash dotnet-helper.sh build

# Run tests
bash dotnet-helper.sh test

# Clean build artifacts
bash dotnet-helper.sh clean

# The built plugin will be in:
# Jellyfin.Plugin.LocalRecs/bin/Debug/net9.0/
```

**Note:** On Windows, use Git Bash or WSL to run the helper script.



## Contributing

Contributions are welcome! For technical details, see [DESIGN.md](DESIGN.md).

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built for the [Jellyfin](https://jellyfin.org/) media server
- Inspired by JellyNext's per-user virtual library architecture
- Uses content-based filtering techniques from recommender systems research

## Support

- **Issues:** Report bugs or request features via [GitHub Issues](https://github.com/rdpharr/jellyfin-plugin-localrecs/issues)
- **Discussions:** Join the conversation on [GitHub Discussions](https://github.com/rdpharr/jellyfin-plugin-localrecs/discussions)
- **Documentation:** See [DESIGN.md](DESIGN.md) for architectural details
