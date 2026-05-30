# Local Recommendations - Design Document

**Version:** 0.1.0 Beta  
**Target:** Jellyfin 10.11.9, .NET 9.0  
**License:** GPLv3

## Overview

Local Recommendations is a Jellyfin server plugin that generates personalized, per-user content recommendations based entirely on local watch history and metadata similarity. The plugin prioritizes privacy, performance, and compatibility across all Jellyfin clients.

**Core Principles:**
- **Privacy-first:** All processing happens locally; no external services or tracking
- **Per-user personalization:** Each user gets recommendations tailored to their viewing history
- **Content-based filtering:** Uses TF-IDF embeddings and cosine similarity for item matching
- **Universal compatibility:** Works on all Jellyfin clients (web, mobile, Roku, etc.)

## Architecture

### High-Level Design

The plugin follows a **layered architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────┐
│              Jellyfin User Interface                     │
│    (Virtual Libraries, Scheduled Tasks, Config UI)      │
└─────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────────────────────────────────────┐
│                   Plugin Layer                           │
│  ┌─────────────────┐  ┌─────────────────────────────┐  │
│  │ Scheduled Tasks │  │   Virtual Library Manager   │  │
│  │ - Refresh Task  │  │   - Symlink generation      │  │
│  │ - Benchmark     │  │   - Directory management    │  │
│  └─────────────────┘  └─────────────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │      Play Status Sync Service (NEW)              │  │
│  │  - Monitors virtual library watch events         │  │
│  │  - Syncs play status to source items             │  │
│  │  - Debounced queue with re-entrancy protection   │  │
│  └──────────────────────────────────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │    Recommendation Refresh Service                 │  │
│  │  - Pipeline orchestration                        │  │
│  │  - Multi-user coordination                       │  │
│  │  └──────────────────────────────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │         Recommendation Engine                     │  │
│  │  - Scoring & Ranking                             │  │
│  │  - Cold-start handling                           │  │
│  │  - Candidate filtering                           │  │
│  └──────────────────────────────────────────────────┘  │
│                                                          │
│  ┌──────────────┐  ┌─────────────┐  ┌───────────────┐ │
│  │   Embedding  │  │ User Profile│  │  Vocabulary   │ │
│  │   Service    │  │   Service   │  │    Builder    │ │
│  └──────────────┘  └─────────────┘  └───────────────┘ │
│                                                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │         Library Analysis Service                  │  │
│  │  - Metadata extraction from Jellyfin             │  │
│  │  - Domain model conversion                       │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────────────────────────────────────┐
│              Jellyfin Server APIs                        │
│   ILibraryManager | IUserManager | IUserDataManager    │
└─────────────────────────────────────────────────────────┘
```

### Core Components

#### 1. Recommendation Refresh Service
**Purpose:** Orchestrates the entire recommendation generation pipeline.

**Responsibilities:**
- Coordinates all services to generate fresh recommendations
- Manages the recommendation refresh workflow for all users
- Integrates with Virtual Library Manager to update symlinks
- Provides entry point for scheduled tasks and manual refreshes

**Workflow:**
1. Extract library metadata via Library Analysis Service
2. Build feature vocabularies via Vocabulary Builder
3. Generate embeddings for all items via Embedding Service
4. For each user:
   - Build user profile via User Profile Service
   - Generate recommendations via Recommendation Engine
   - Update virtual library symlinks via Virtual Library Manager
5. Log completion and provide feedback to admin

**Key insight:** This service is the "main entry point" that ties together all the domain services into a cohesive recommendation pipeline.

#### 2. Library Analysis Service
**Purpose:** Bridges the gap between Jellyfin's internal data structures and the plugin's domain models.

**Responsibilities:**
- Query Jellyfin's library via `ILibraryManager`
- Extract metadata (genres, actors, directors, tags, ratings, release year)
- Convert Jellyfin BaseItem types to domain models (`MediaItemMetadata`)
- Abstract away Jellyfin-specific types for easier testing

**Output:** List of `MediaItemMetadata` objects containing:
- Item ID and name
- Media type (Movie or Series)
- Genres, actors, directors, tags
- Community rating and release year
- Original Jellyfin path for symlink generation

**Key insight:** By creating a clean abstraction layer (`MediaItemMetadata`), the plugin can be developed and tested independently of Jellyfin's complex internal APIs.

#### 3. Vocabulary Builder
**Purpose:** Build feature vocabularies from library metadata to enable TF-IDF computation.

**Responsibilities:**
- Extract unique features from all library items (genres, actors, directors, tags, decades)
- Compute document frequency for each feature (how many items contain it)
- Apply vocabulary limits to most frequent features (default: top 500 actors/tags)
- Generate `FeatureVocabulary` object with term-to-index mappings

**Algorithm:**
1. Collect all unique terms from library metadata
2. Count document frequency for each term (DF)
3. Sort by frequency and apply limits
4. Assign unique index to each term for vector representation

**Output:** `FeatureVocabulary` containing:
- Term-to-index mappings for all feature types
- Document frequencies for IDF calculation
- Total document count for normalization

**Vocabulary Limits (configurable):**
- Genres: No limit (typically <100 unique)
- Actors: Top 500 by frequency
- Directors: No limit (typically <500 unique)
- Tags: Top 500 by frequency
- Decades: No limit (typically 10-15 unique, e.g., "1960s", "1970s", ..., "2020s", "Unknown")

**Decade Feature:**
- Derived automatically from `ReleaseYear` field (no additional metadata required)
- Format: Decade strings like "1980s", "1990s", "2000s", etc.
- Items without release year are assigned "Unknown" decade
- Provides temporal similarity for era-based recommendations (e.g., 80s action movies, 90s comedies)
- Replaces the previous continuous year normalization feature with categorical decade grouping

**Why limit vocabularies?**
- Reduces embedding dimensionality from 5000+ to ~1200-1500
- Focuses on commonly-appearing actors/tags that matter for recommendations
- Significantly improves memory usage and computation speed
- Rare features (appearing in 1-2 items) provide little value for similarity

#### 4. Embedding Service
**Purpose:** Transform media metadata into numerical vector representations.

**Algorithm:** TF-IDF (Term Frequency-Inverse Document Frequency)
- **Categorical features** (genres, actors, directors, tags, decades): TF-IDF vectors
- **Numerical features** (ratings): Normalized scalars
- **Output:** Fixed-length embedding vector per item (~1200-1500 dimensions)

**Vector Layout:**
```
[Genres (N) | Actors (M) | Directors (P) | Tags (Q) | Decades (~12) | Ratings (2)]
```

**Decade Encoding:**
- Each item is assigned to exactly one decade based on its release year
- Decades are treated as categorical features (like genres or tags)
- Binary TF (present=1, absent=0) with IDF weighting
- Common decades (e.g., 2000s with many items) get lower IDF weights
- Rare decades (e.g., 1920s with few items) get higher IDF weights
- This allows recommendations to favor items from similar time periods

**Why TF-IDF?**
- Emphasizes distinctive features (rare actors/genres/decades) over common ones
- Computationally efficient (linear in library size)
- Interpretable and debuggable
- No training data required (works from day one)

#### 5. User Profile Service
**Purpose:** Aggregate a user's watch history into a single "taste vector."

**Weighting Factors:**
- **Favorites:** 2.0x boost (configurable)
- **Recent watch emphasis:** decay² amplification — `decay × (1 + recentWatchBoost × decay)` — disproportionately amplifies recently watched items without relying on unreliable play count data (default: 1.0, configurable)
- **Recency decay:** Exponential decay with configurable half-life (default: 365 days)

**Output:** Normalized user profile vector (same dimensionality as item embeddings)

**Rating Statistics:**
- Computes average community rating (0-10 scale) and critic rating (0-100 scale) from watched items
- Calculates standard deviation for both rating types to understand user's rating tolerance
- Used for optional rating proximity weighting in recommendation scoring

**Watch History Detection:**
- Only fully-watched items (Played = true) count toward profile
- For movies: item must be fully played
- For series: all episodes must be watched
- Partially watched or abandoned content is excluded from taste profile
- User-configurable minimum watch threshold (default: 3 items)

#### 6. Recommendation Engine
**Purpose:** Score and rank unwatched candidates based on similarity to user profile.

**Candidate Filtering:**
- Excludes fully watched items (Played = true)
- Excludes items with any playback progress (PlaybackPositionTicks > 0)
  - Prevents re-recommending items user is currently watching
  - Auto-removed items can reappear if user marks as unwatched (resets progress to 0)
- Excludes items with insufficient metadata (no genres AND no actors)
  - Ensures reliable similarity scores for TF-IDF/cosine similarity
  - Items must have at least one genre OR at least one actor to be considered
- Excludes abandoned TV series (configurable threshold, default: 90 days since last watched)

**Scoring Algorithm:**
1. Compute cosine similarity between user taste vector and each candidate item embedding
2. Optionally blend with rating proximity score (if enabled):
   - **Rating Proximity:** Measures how close an item's ratings are to the user's average ratings
   - **Community Rating Proximity:** Based on 0-10 scale (e.g., IMDb/TMDb ratings)
   - **Critic Rating Proximity:** Based on 0-100 scale (e.g., Rotten Tomatoes/Metacritic)
   - **Blending:** `final_score = (1 - weight) * cosine_similarity + weight * rating_proximity`
   - **Default:** Disabled (pure content similarity)
   - **When enabled:** Default weight = 0.2 (20% rating proximity, 80% content similarity)
3. Rank candidates by descending similarity score
4. Return top N items (configurable, default: 25)

**Rating Proximity Feature (Optional):**
- **Purpose:** Boosts items with ratings similar to user's viewing history
- **Use case:** Users who prefer highly-rated content get more highly-rated recommendations
- **Calculation:** For each rating type (community/critic), compute proximity as `1 - (|item_rating - user_avg_rating| / scale)`
- **Fallback:** If item or user lacks ratings, uses neutral value (0.5)
- **Configurable:** Can be enabled/disabled and weight adjusted (0.0 to 1.0) via plugin settings

**Cold-start strategy:**
- Users with <3 watched items receive top-rated content from the library
- No personalization until sufficient watch history exists

**Rating Proximity Enhancement:**

The Recommendation Engine supports an optional rating proximity feature that blends content-based similarity with rating-based similarity. This helps users discover items that match both their content preferences and their rating preferences.

**How It Works:**
1. **User Profile Service** computes average ratings from watch history:
   - Average community rating (0-10 scale) from watched items
   - Average critic rating (0-100 scale) from watched items
   - Standard deviation for each rating type (future use)

2. **Recommendation Engine** computes proximity scores:
   - For each candidate item, compute rating difference from user's averages
   - Convert difference to proximity score: `1 - (absolute_difference / scale)`
   - Average community and critic proximities for final rating proximity score
   - If item or user lacks ratings, use neutral value (0.5)

3. **Final Score Blending:**
   - `final_score = (1 - weight) * cosine_similarity + weight * rating_proximity`
   - **weight = 0.0:** Pure content similarity (default)
   - **weight = 0.2:** 80% content, 20% rating proximity (recommended)
   - **weight = 1.0:** Pure rating matching (not recommended)

**Example:**
- User's average community rating: 7.5/10
- Candidate item rating: 8.0/10
- Rating difference: 0.5
- Community proximity: 1 - (0.5 / 10) = 0.95 (very close)
- If content similarity = 0.80 and weight = 0.2:
  - Final score = 0.8 * 0.80 + 0.2 * 0.95 = 0.64 + 0.19 = 0.83

**When to Enable:**
- Users who strongly prefer highly-rated or critically-acclaimed content
- Libraries with comprehensive rating metadata
- Users with consistent rating patterns in their watch history

**When to Disable (Default):**
- Pure content-based recommendations based on genres, actors, directors
- Libraries with sparse or inconsistent rating metadata
- Users with diverse rating tolerances

#### 7. Virtual Library Manager
**Purpose:** Expose recommendations as per-user virtual libraries with complete metadata.

**Implementation:** Filesystem symlink-based approach. The virtual library mirrors the source
media structure using symbolic links. Each symlink has the source file's real extension, so
Jellyfin's media pipeline treats it as regular media and transcoding, probing, and artwork
discovery all work natively.

**Historical note:** Prior versions (≤0.5.3) used `.strm` files containing the source path.
Jellyfin 10.11.7 shipped security fix [GHSA-j2hf-x4q5-47j3](https://github.com/jellyfin/jellyfin/security/advisories/GHSA-j2hf-x4q5-47j3)
restricting `.strm` files to remote URL schemes only (`http`, `https`, `rtsp`, `rtp`). Local paths
were silently dropped, breaking transcoded playback. Symlinks bypass the `.strm` parser entirely.

> ⚠️ **Windows hosts:** symlink creation requires either Administrator privileges **or** Windows
> Developer Mode enabled (Settings → Privacy & security → For developers → Developer Mode).
> Docker-on-Linux deployments and native Linux installs are unaffected. See the README
> troubleshooting section for remediation.

**Directory Structure:**
```
{plugin-data}/virtual-libraries/
├── {userId1}/
│   ├── movies/
│   │   └── Movie Title (2020) [tmdbid-12345]/
│   │       ├── Movie Title (2020) [tmdbid-12345].mkv    # symlink → source media
│   │       ├── Movie Title (2020) [tmdbid-12345]-trailer.mp4  # symlink (if exists)
│   │       ├── poster.jpg                                # symlink → source artwork
│   │       └── fanart.jpg                                # symlink → source artwork
│   └── tv/
│       └── Show Title (2019) [tvdbid-67890]/
│           ├── poster.jpg                                # symlink → series artwork
│           ├── Season 01/
│           │   ├── Show - S01E01 - Episode Title.mkv    # symlink → episode source
│           │   └── Show - S01E02 - Episode Title.mkv
│           └── Season 02/
│               └── Show - S02E01 - Episode Title.mkv
└── {userId2}/
    ├── movies/
    └── tv/
```

**Artwork:** Jellyfin's native scanner picks up sibling `poster.jpg`, `fanart.jpg`, etc.
The plugin symlinks any of the following from the source folder into the virtual folder so
custom artwork on source items (e.g. French posters, user uploads) carries through without a
dedicated copy step: `poster.{jpg,png,webp}`, `folder.{jpg,png}`, `fanart.{jpg,png}`,
`backdrop.{jpg,png}`, `landscape.{jpg,png}`, `banner.{jpg,png}`, `logo.png`, `clearart.png`,
`clearlogo.png`, `disc.png`, `thumb.jpg`.

**Trailers:** Jellyfin's trailer discovery (`trailers/` subfolder and `-trailer` suffix
siblings) is handled by symlinking the trailer files by name — no custom scanning logic.

**Sync Algorithm:** Clear-and-recreate
1. Delete the user's media-type directory recursively (`Directory.Delete(recursive: true)` —
   removes symlinks without following them)
2. Recreate the directory
3. Create fresh symlinks for current recommendations (media + artwork + trailers)
4. Trigger Jellyfin library scan to update database

**Why clear-and-recreate instead of diff-based sync?**
- Simpler implementation with fewer edge cases
- Relies on Jellyfin's library scanner to clean up orphaned database entries
- No direct database manipulation required (respects Jellyfin's internal APIs)

#### 8. Play Status Sync Service
**Purpose:** Sync play status from virtual library items to source library items so that watch state is reflected in the real library.

**Problem Solved:**
- When users mark items as played/favorite in virtual recommendation libraries, the source library should reflect those changes
- Ensures the next recommendation refresh correctly excludes watched items

**Implementation:**
- **Event-driven:** Subscribes to `UserDataSaved` events from Jellyfin
- **SaveReason filtering:** Only processes meaningful state changes (`PlaybackFinished`, `TogglePlayed`, `UpdateUserRating`, `Import`, `UpdateUserData`). Ignores `PlaybackStart` and `PlaybackProgress` events to avoid a storm of database writes during active playback.
- **Debounced queue:** 5-second debounce to coalesce rapid state changes
- **Re-entrancy protection:** Prevents infinite loops when `SaveUserData` triggers additional `UserDataSaved` events
- **Thread-safe disposal:** Uses `ManualResetEvent` to ensure clean shutdown

**Sync Flow:**
1. User finishes watching or toggles played/favorite on a virtual library item
2. `OnUserDataSaved` fires with a non-transient `SaveReason`
3. Item is queued for sync (debounced, keyed by user+item to coalesce duplicates)
4. After 5s, `FlushQueue` resolves the symlink target to find the source item path
5. Play status (Played, PlayCount, PlaybackPositionTicks, LastPlayedDate, IsFavorite) is copied from virtual item to source item

**Deferred Removal:**
- Virtual library items are **never removed by event handlers**
- Watched items remain in recommendation libraries until the next scheduled refresh
- The refresh task does a full clear-and-rebuild via `VirtualLibraryManager.SyncRecommendations()`, which naturally excludes watched items
- This avoids playback crashes caused by removing items mid-playback (see Known Limitations)

**Lifecycle:**
- Initialized eagerly during plugin startup via `VirtualLibraryInitializer` dependency injection
- Ensures service is instantiated and event handlers are registered before any user activity
- Disposed properly when plugin shuts down, flushing any pending updates

#### 9. Scheduled Tasks
**Purpose:** Generate fresh recommendations through scheduled updates.

**Update Model:**
- **Scheduled Task:** Admin-configurable schedule (recommended: daily at 4:00 AM)
- **Manual Refresh:** Available anytime via Scheduled Tasks UI
- **Fresh computation:** Embeddings and recommendations computed from scratch on every run
- **No caching:** Ensures recommendations always reflect current watch history
- **No watch event handling:** Simplicity over real-time updates

**Scheduled Task Flow:**
1. Compute fresh embeddings for all library items
2. Generate recommendations for all users based on current watch history
3. Clear old symlinks and create new ones for each user
4. Log instructions for manual library scan (automatic scanning disabled due to scan-timing issues)

**Note:** Automatic library scanning is intentionally disabled. Users should manually scan their recommendation libraries or rely on scheduled library scans.

## Jellyfin API Constraints

### What the Plugin Can Do
- ✅ Query library items via `ILibraryManager`
- ✅ Query user data (watch history, favorites) via `IUserDataManager`
- ✅ Create directories in plugin data folder
- ✅ Register scheduled tasks
- ✅ Provide configuration UI (HTML/JavaScript)

### What the Plugin Cannot Do
- ❌ **Create Jellyfin libraries programmatically** - No API exists for library creation
- ❌ **Assign library permissions programmatically** - Permission management is admin-only
- ❌ **Inject items into existing libraries** - Libraries are tied to physical directories
- ❌ **Create custom UI sections** - Limited to plugin configuration pages

### Design Workarounds

**Problem:** Cannot create libraries automatically  
**Solution:** Plugin creates directories and provides clear setup instructions in:
- Jellyfin logs (at startup)
- Plugin configuration UI (Setup tab with copy-paste workflow)
- README documentation

**Problem:** Cannot assign permissions automatically  
**Solution:** Leverage Jellyfin's built-in library access control:
- Each user gets their own physical directory
- Admin manually assigns permissions per user
- Per-user isolation is handled by Jellyfin itself

**Problem:** Limited UI extensibility  
**Solution:** Rich configuration page with tabs:
- Setup guide with paths and copy buttons
- Settings for all tunable parameters
- Actions for manual refresh and benchmarking

## Data Flow

### Initial Setup (One-Time, Manual)
1. Admin installs plugin
2. Plugin creates per-user directories in plugin data folder
3. Plugin logs setup instructions with exact paths
4. Admin creates Jellyfin libraries pointing to plugin directories (manual)
5. Admin assigns library permissions per user (manual)
6. Admin triggers initial recommendation refresh (manual)

### Recommendation Refresh (Automatic Daily / Manual)
1. Scheduled task triggers Recommendation Refresh Service (default: daily 4:00 AM, or manual)
2. Library Analysis Service scans Jellyfin library and extracts metadata
3. Vocabulary Builder creates feature vocabularies from library metadata
4. Embedding Service computes TF-IDF vectors for all items
5. For each user:
   - User Profile Service aggregates watch history into taste vector and computes rating statistics
   - Recommendation Engine scores all unwatched candidates using content similarity (and optionally rating proximity)
   - Excludes items with playback progress
   - Top N items selected per media type (movies, TV)
   - Virtual Library Manager clears old symlinks and creates new ones for this user
6. Recommendation Refresh Service logs completion
7. Users manually scan recommendation libraries (or wait for scheduled scan) to see updated recommendations

**Play Status Sync During Usage:**
- When a user finishes watching or toggles played/favorite on a virtual library item, PlayStatusSyncService syncs the state to the source library item
- Transient playback events (start, progress) are ignored to avoid database write storms
- Watched items remain in the recommendation library until the next scheduled refresh naturally excludes them

**Note:** Embeddings are computed fresh on every refresh to ensure recommendations always reflect the current watch history. No caching is performed.

## Performance Considerations

### Target Environment
- **Library size:** 2,000 items (design target)
- **Users:** 2+ users
- **Server:** 10GB RAM, 4 CPU cores (typical NAS)

### Performance Targets
- Full refresh (all users): <2 minutes
- Vocabulary build: <2 seconds
- Item embeddings (2k items): <30 seconds
- User profile (100 watched): <100ms
- Recommendation scoring: <500ms per user
- Peak memory: <100MB

### Optimization Strategies
- **Vocabulary limits:** Top 500 actors/tags by frequency (configurable)
- **Fresh computation:** Embeddings computed on every refresh to ensure recommendations reflect current watch history
- **Clear-and-recreate:** Virtual library symlinks completely replaced each refresh (no incremental sync complexity)

### Scalability
- **Up to 10k items:** Expected to work well with default settings
- **10k-30k items:** May need vocabulary limits and scheduled-only mode
- **>30k items:** Requires performance tuning, possible architecture changes

## Configuration

All settings exposed via plugin configuration UI and stored in Jellyfin's plugin configuration system.

### Key Settings
- **Recommendation counts:** Movies and TV (default: 25 each)
- **Update schedule:** Daily at 4:00 AM by default (customizable via Scheduled Tasks UI)
- **Weighting factors:** Favorite boost (2x), recent watch emphasis (decay² amplification, default 1.0), recency decay half-life (365 days)
- **Performance tuning:** Vocabulary limits for actors, directors, and tags
- **Cold-start threshold:** Minimum watched items for personalization (default: 3)
- **Series filtering:** Exclude abandoned series from recommendations (configurable threshold)
- **Rating proximity:** Enable/disable rating-based scoring and adjust blending weight (default: disabled)

## Testing Strategy

### Unit Tests
- Pure utility classes: 100% coverage (vector math, TF-IDF computation, weight calculations)
- Domain services: >80% coverage (embedding, user profiles, recommendation engine)
- Test fixtures for realistic scenarios (sci-fi fan, diverse tastes, new user)

### Integration Tests
- Live server tests validating end-to-end pipelines
- Virtual library file operations

### Performance Tests
- Benchmark task included in plugin
- Measures all critical paths (vocabulary, embeddings, scoring)
- Validates against performance targets
- Reports to admin via UI

## Known Limitations

### Virtual Library Metadata Display

**Limitation:** Items in the virtual recommendation libraries do not display full text metadata (runtime, ratings, genres, cast, etc.) in the Jellyfin UI.

**Why this happens:**
- Virtual library items are symlinks under a distinct library root, so Jellyfin treats them as separate `BaseItem`s from the source media
- Metadata providers run against the virtual item's library root, which may be configured differently than the source library (e.g., no TMDB lookups)

**What DOES work:**
- **Poster/backdrop images:** Jellyfin picks up `poster.jpg`, `fanart.jpg`, etc. that the plugin symlinks from the source folder, preserving any custom artwork
- **Playback:** All media plays correctly through the symlinked files (direct play and transcoding both work, unlike the old `.strm` approach on Jellyfin ≥10.11.7)

**What does NOT work:**
- Runtime/duration is not displayed for recommendations (shows as unknown or 0:00)
- Ratings, genres, and cast information may not appear in the UI
- Any text metadata customizations from source items

**Workarounds considered but not viable for text metadata:**
- Direct database manipulation: Would bypass Jellyfin's APIs and risk data corruption
- Custom metadata providers: Would require significant additional complexity
- Jellyfin API calls post-scan: Fragile timing, items may not exist yet when called

**User impact:**
- Users can still play recommendations normally
- Custom posters and backdrops are preserved in recommendation libraries
- Full metadata is visible once playback begins (from the source item)
- This is a cosmetic limitation that doesn't affect recommendation quality or playback

### Duplicate "Continue Watching" / "Next Up" Entries

**Limitation:** Partially watched recommendations appear twice in "Continue Watching" (movies) or "Next Up" (TV series) — once for the virtual symlinked item and once for the source media file.

**Why this happens:**
- The virtual symlink and the source media file are separate items in Jellyfin's database (different paths → different rows)
- Both accumulate independent playback position when watched
- PlayStatusSyncService syncs the final state (Played, PlayCount, etc.) from virtual to source, but Jellyfin tracks playback position on both items during active playback
- Removing the virtual item mid-playback causes crashes (Jellyfin loses the active stream reference), so removal is deferred to the next recommendation refresh

**When it resolves:**
- On the next scheduled recommendation refresh, `SyncRecommendations()` does a full clear-and-rebuild that excludes watched/in-progress items
- If the user finishes watching (Played=true), the item won't appear in the next refresh's recommendations

**Workaround:**
- Users can manually trigger "Refresh Local Recommendations" from Scheduled Tasks to clear watched items sooner

### Other Limitations

- **No automatic library creation:** Admin must manually create Jellyfin libraries pointing to plugin directories
- **No automatic permission assignment:** Admin must manually assign library access per user
- **Manual library scan required:** After recommendation refresh, users should scan recommendation libraries to see updates

## Security & Privacy

**Privacy guarantees:**
- All computation happens on local server
- No data sent to external services
- No tracking or telemetry
- User data isolated per Jellyfin's permission model

**Security considerations:**
- Input sanitization for filenames (prevent path traversal)
- Thread-safe file operations (per-user locking for symlink writes)
- Proper error handling to prevent information leakage in logs
- Respects Jellyfin's user permission model

## Summary

Local Recommendations provides a privacy-conscious, efficient recommendation system for Jellyfin that works within the constraints of the Jellyfin plugin API. By using content-based filtering (TF-IDF + cosine similarity) and per-user virtual libraries (filesystem symlinks), the plugin delivers personalized recommendations accessible from any Jellyfin client while maintaining complete data privacy.

The architecture is designed for maintainability, testability, and performance on typical home server hardware, with clear extension points for future enhancements.
