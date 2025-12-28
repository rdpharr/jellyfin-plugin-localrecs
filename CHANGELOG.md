# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
