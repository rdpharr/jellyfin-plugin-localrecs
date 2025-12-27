# Local Recommendations - Design Document

**Version:** 0.1.0 Beta  
**Target:** Jellyfin 10.11.5, .NET 9.0  
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
│  │ - Refresh Task  │  │   - .strm file generation   │  │
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
- Integrates with Virtual Library Manager to update .strm files
- Provides entry point for scheduled tasks and manual refreshes

**Workflow:**
1. Extract library metadata via Library Analysis Service
2. Build feature vocabularies via Vocabulary Builder
3. Generate embeddings for all items via Embedding Service
4. For each user:
   - Build user profile via User Profile Service
   - Generate recommendations via Recommendation Engine
   - Update virtual library .strm files via Virtual Library Manager
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
- Original Jellyfin path for .strm file generation

**Key insight:** By creating a clean abstraction layer (`MediaItemMetadata`), the plugin can be developed and tested independently of Jellyfin's complex internal APIs.

#### 3. Vocabulary Builder
**Purpose:** Build feature vocabularies from library metadata to enable TF-IDF computation.

**Responsibilities:**
- Extract unique features from all library items (genres, actors, directors, tags)
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

**Why limit vocabularies?**
- Reduces embedding dimensionality from 5000+ to ~1200-1500
- Focuses on commonly-appearing actors/tags that matter for recommendations
- Significantly improves memory usage and computation speed
- Rare features (appearing in 1-2 items) provide little value for similarity

#### 4. Embedding Service
**Purpose:** Transform media metadata into numerical vector representations.

**Algorithm:** TF-IDF (Term Frequency-Inverse Document Frequency)
- **Categorical features** (genres, actors, directors, tags): TF-IDF vectors
- **Numerical features** (ratings, release year): Normalized scalars
- **Output:** Fixed-length embedding vector per item (~1200-1500 dimensions)

**Why TF-IDF?**
- Emphasizes distinctive features (rare actors/genres) over common ones
- Computationally efficient (linear in library size)
- Interpretable and debuggable
- No training data required (works from day one)

#### 5. User Profile Service
**Purpose:** Aggregate a user's watch history into a single "taste vector."

**Weighting Factors:**
- **Favorites:** 2.0x boost (configurable)
- **Rewatches:** 1.5x boost (configurable)
- **Recency decay:** Exponential decay with configurable half-life (default: 365 days)

**Output:** Normalized user profile vector (same dimensionality as item embeddings)

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
2. Rank candidates by descending similarity score
3. Return top N items (configurable, default: 25)

**Cold-start strategy:**
- Users with <3 watched items receive top-rated content from the library
- No personalization until sufficient watch history exists

#### 7. Virtual Library Manager
**Purpose:** Expose recommendations as per-user virtual libraries with complete metadata.

**Implementation:** `.strm` file-based approach with NFO metadata files and local trailer support.

**Directory Structure:**
```
{plugin-data}/virtual-libraries/
├── {userId1}/
│   ├── movies/
│   │   └── Movie Title (2020) [tmdbid-12345]/
│   │       ├── Movie Title (2020) [tmdbid-12345].strm    # Main movie file
│   │       ├── Movie Title (2020) [tmdbid-12345].nfo     # Metadata (runtime, etc.)
│   │       └── Movie Title (2020) [tmdbid-12345]-trailer.strm  # Trailer (if exists)
│   └── tv/
│       └── Show Title (2019) [tvdbid-67890]/
│           ├── tvshow.nfo                                # Series metadata
│           ├── Show Title (2019) [tvdbid-67890]-trailer.strm   # Series trailer (if exists)
│           ├── Season 01/
│           │   ├── Show - S01E01 - Episode Title.strm    # Episode file
│           │   ├── Show - S01E01 - Episode Title.nfo     # Episode metadata
│           │   └── Show - S01E02 - Episode Title.strm
│           └── Season 02/
│               └── Show - S02E01 - Episode Title.strm
└── {userId2}/
    ├── movies/
    └── tv/
```

**Important Notes on File Types:**
- **Movies:** Folder containing `.strm`, `.nfo`, and optional `-trailer.strm` files
- **TV Series:** Folder structure containing:
  - Series folder: `Show Name (Year) [tvdbid-12345]/`
  - `tvshow.nfo`: Series-level metadata
  - Series trailers: `Show Name-trailer.strm` (if source has local trailers)
  - Season subfolders: `Season 01/`, `Season 02/`, `Specials/`
  - Episode files: `.strm` and `.nfo` pairs for each episode

**Why .strm files?**
- Standard Jellyfin mechanism for virtual/remote content
- Each file contains the full path to the original media file
- Jellyfin handles playback automatically (including series navigation)
- No duplication of media files
- Works across all Jellyfin clients
- Preserves series/season/episode hierarchy for TV shows

**Why .nfo files?**
- Provides complete metadata (runtime, ratings, genres, etc.) to Jellyfin
- Essential for clients like Roku that require local metadata
- Contains runtime in minutes for proper content length display
- Follows Jellyfin's standard NFO format (Kodi-compatible)

**Why trailer .strm files?**
- Enables local trailer playback on all clients (including Roku)
- Uses `-trailer` suffix naming convention (Jellyfin standard)
- Points to source trailer files on disk (not remote URLs)
- Remote trailer URLs don't work on all clients (e.g., Roku)

**Sync Algorithm:** Clear-and-recreate
1. Delete all existing files and folders
2. Recreate directory structure
3. Create fresh .strm, .nfo, and trailer files for current recommendations
4. Trigger Jellyfin library scan to update database

**Why clear-and-recreate instead of diff-based sync?**
- Simpler implementation with fewer edge cases
- Avoids stale metadata issues in Jellyfin's database
- Relies on Jellyfin's library scanner to clean up orphaned database entries
- No direct database manipulation required (respects Jellyfin's internal APIs)

#### 7a. NFO Writer Service
**Purpose:** Generate NFO metadata files for virtual library items.

**Responsibilities:**
- Generate movie NFO files with runtime, ratings, genres, and provider IDs
- Generate series NFO files (`tvshow.nfo`) with series-level metadata
- Generate episode NFO files with episode-specific details
- Proper XML formatting following Jellyfin/Kodi NFO standards

**NFO Content (Movies):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<movie>
    <title>Movie Title</title>
    <year>2020</year>
    <runtime>120</runtime>           <!-- Runtime in minutes -->
    <plot>Movie description...</plot>
    <rating>7.5</rating>
    <mpaa>PG-13</mpaa>
    <genre>Action</genre>
    <genre>Adventure</genre>
    <studio>Studio Name</studio>
    <tmdbid>12345</tmdbid>
    <imdbid>tt1234567</imdbid>
</movie>
```

**NFO Content (TV Series - tvshow.nfo):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<tvshow>
    <title>Show Title</title>
    <year>2019</year>
    <plot>Series description...</plot>
    <status>Continuing</status>
    <tvdbid>67890</tvdbid>
</tvshow>
```

**NFO Content (Episodes):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<episodedetails>
    <title>Episode Title</title>
    <season>1</season>
    <episode>1</episode>
    <aired>2019-01-15</aired>
    <runtime>45</runtime>
    <plot>Episode description...</plot>
    <showtitle>Show Title</showtitle>
</episodedetails>
```

**Key insight:** NFO files ensure that virtual library items display complete metadata including runtime/content length, which is critical for user experience on all clients.

#### 8. Play Status Sync Service
**Purpose:** Auto-remove recommendations when users start watching them, preventing duplicate "Continue Watching" entries.

**Problem Solved:**
- When users start watching a recommendation, both the virtual library item and source library item appear in "Continue Watching"
- This creates confusion with duplicate entries for the same content
- Once a user starts watching, the recommendation has served its purpose (discovery)

**Implementation:**
- **Event-driven:** Subscribes to `UserDataSaved` events from Jellyfin
- **Auto-removal on watch:** When playback position > 0, immediately removes the virtual library .strm file
- **Debounced queue:** 5-second debounce for non-removal operations (favorites, etc.)
- **Re-entrancy protection:** Prevents infinite loops when operations trigger additional events
- **Thread-safe disposal:** Uses `ManualResetEvent` to ensure clean shutdown

**Auto-Removal Mechanism:**
1. User starts watching item in virtual library (any playback progress > 0)
2. Event handler detects playback progress
3. Service immediately removes virtual library .strm file (movies) or entire series folder (TV)
4. Triggers library scan to update Jellyfin's database
5. Only source library item remains with watch status
6. Source item appears in "Continue Watching" without duplicate

**Removal Criteria:**
- **Threshold:** Any playback progress (PlaybackPositionTicks > 0)
- **Movies:** Deletes individual .strm file
- **TV Series:** Deletes entire series folder on first episode watch
- **Automatic:** No configuration needed, always enabled
- **Re-recommendation:** Items can reappear in future recommendations if user marks as unwatched

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
3. Clear old .strm files and create new ones for each user
4. Log instructions for manual library scan (automatic scanning disabled due to .strm file removal issues)

**Note:** Automatic library scanning is intentionally disabled. Jellyfin's `ValidateMediaLibrary()` can remove .strm files before metadata is fetched. Users should manually scan their recommendation libraries or rely on scheduled library scans.

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
   - User Profile Service aggregates watch history into taste vector
   - Recommendation Engine scores all unwatched candidates (excludes items with playback progress)
   - Top N items selected per media type (movies, TV)
   - Virtual Library Manager clears old .strm files and creates new ones for this user
6. Recommendation Refresh Service logs completion
7. Users manually scan recommendation libraries (or wait for scheduled scan) to see updated recommendations

**Auto-Removal During Usage:**
- When user starts watching a recommendation (any playback > 0), PlayStatusSyncService immediately removes the virtual library item
- This prevents duplicate "Continue Watching" entries
- Items can reappear in future recommendation refreshes if user marks as unwatched

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
- **Clear-and-recreate:** Virtual library .strm files completely replaced each refresh (no incremental sync complexity)

### Scalability
- **Up to 10k items:** Expected to work well with default settings
- **10k-30k items:** May need vocabulary limits and scheduled-only mode
- **>30k items:** Requires performance tuning, possible architecture changes

## Configuration

All settings exposed via plugin configuration UI and stored in Jellyfin's plugin configuration system.

### Key Settings
- **Recommendation counts:** Movies and TV (default: 25 each)
- **Update schedule:** Daily at 4:00 AM by default (customizable via Scheduled Tasks UI)
- **Weighting factors:** Favorite boost (2x), rewatch boost (1.5x), recency decay half-life (365 days)
- **Performance tuning:** Vocabulary limits for actors, directors, and tags
- **Cold-start threshold:** Minimum watched items for personalization (default: 3)
- **Series filtering:** Exclude abandoned series from recommendations (configurable threshold)

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

## Future Enhancements

**Potential improvements for future versions:**
- Collaborative filtering (optional, privacy-preserving)
- Genre/actor/director exclusion filters
- Time-of-day or seasonal recommendations
- Advanced analytics dashboard
- Learned embeddings (neural networks) to replace TF-IDF
- Support for other media types (music, books)

## Security & Privacy

**Privacy guarantees:**
- All computation happens on local server
- No data sent to external services
- No tracking or telemetry
- User data isolated per Jellyfin's permission model

**Security considerations:**
- Input sanitization for filenames (prevent path traversal)
- Thread-safe file operations (per-user locking for .strm file writes)
- Proper error handling to prevent information leakage in logs
- Respects Jellyfin's user permission model

## Summary

Local Recommendations provides a privacy-conscious, efficient recommendation system for Jellyfin that works within the constraints of the Jellyfin plugin API. By using content-based filtering (TF-IDF + cosine similarity) and per-user virtual libraries (.strm files), the plugin delivers personalized recommendations accessible from any Jellyfin client while maintaining complete data privacy.

The architecture is designed for maintainability, testability, and performance on typical home server hardware, with clear extension points for future enhancements.
