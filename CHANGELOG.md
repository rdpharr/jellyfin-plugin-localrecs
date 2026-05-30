# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
