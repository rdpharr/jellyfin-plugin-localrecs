using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using BaseItemKind = Jellyfin.Data.Enums.BaseItemKind;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Manages virtual library .strm files for per-user recommendations.
    /// Creates and maintains .strm files in per-user directories that point to original media files.
    /// </summary>
    public class VirtualLibraryManager
    {
        private readonly ILogger<VirtualLibraryManager> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly NfoWriter _nfoWriter;
        private readonly string _virtualLibraryBasePath;

        /// <summary>
        /// Lock object for thread-safe file operations per user.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, object> _userLocks = new ConcurrentDictionary<Guid, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualLibraryManager"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager for media access.</param>
        /// <param name="nfoWriter">NFO file writer for metadata.</param>
        /// <param name="virtualLibraryBasePath">Base path for virtual libraries.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public VirtualLibraryManager(
            ILogger<VirtualLibraryManager> logger,
            ILibraryManager libraryManager,
            NfoWriter nfoWriter,
            string virtualLibraryBasePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _nfoWriter = nfoWriter ?? throw new ArgumentNullException(nameof(nfoWriter));
            _virtualLibraryBasePath = virtualLibraryBasePath ?? throw new ArgumentNullException(nameof(virtualLibraryBasePath));
        }

        /// <summary>
        /// Gets the virtual library path for a specific user and media type.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="mediaType">Media type (Movie or Series).</param>
        /// <returns>Full path to the user's virtual library directory.</returns>
        public string GetUserLibraryPath(Guid userId, MediaType mediaType)
        {
            var subfolder = mediaType == MediaType.Movie ? "movies" : "tv";
            return Path.Combine(_virtualLibraryBasePath, userId.ToString(), subfolder);
        }

        /// <summary>
        /// Ensures the virtual library directories exist for a user.
        /// Creates both movies and tv subdirectories.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="username">Username for logging purposes.</param>
        /// <returns>True if directories were created successfully, false otherwise.</returns>
        public bool EnsureUserDirectoriesExist(Guid userId, string? username = null)
        {
            var displayName = username ?? userId.ToString();

            try
            {
                var moviePath = GetUserLibraryPath(userId, MediaType.Movie);
                var tvPath = GetUserLibraryPath(userId, MediaType.Series);

                Directory.CreateDirectory(moviePath);
                Directory.CreateDirectory(tvPath);

                _logger.LogDebug(
                    "Ensured virtual library directories exist for user {Username} ({UserId})",
                    displayName,
                    userId);

                return true;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to create directories for user {Username} ({UserId})", displayName, userId);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied creating directories for user {Username} ({UserId})", displayName, userId);
                return false;
            }
        }

        /// <summary>
        /// Deletes all virtual library directories for a user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="username">Username for logging purposes.</param>
        /// <returns>True if directories were deleted or didn't exist, false on error.</returns>
        public bool DeleteUserDirectories(Guid userId, string? username = null)
        {
            var displayName = username ?? userId.ToString();
            var userPath = Path.Combine(_virtualLibraryBasePath, userId.ToString());

            if (!Directory.Exists(userPath))
            {
                _logger.LogDebug(
                    "No virtual library directory found for user {Username} ({UserId})",
                    displayName,
                    userId);
                return true;
            }

            try
            {
                Directory.Delete(userPath, recursive: true);
                _logger.LogDebug(
                    "Deleted virtual library directories for user {Username} ({UserId})",
                    displayName,
                    userId);
                return true;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete directories for user {Username} ({UserId})", displayName, userId);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied deleting directories for user {Username} ({UserId})", displayName, userId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting directories for user {Username} ({UserId})", displayName, userId);
                return false;
            }
        }

        /// <summary>
        /// Updates recommendations by clearing all existing .strm files and creating new ones.
        /// This ensures recommendations always match the latest generation without sync issues.
        /// This method is thread-safe per user.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="recommendations">List of recommended items.</param>
        /// <param name="mediaType">Media type (Movie or Series).</param>
        /// <returns>Number of files created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when recommendations is null.</exception>
        public int SyncRecommendations(
            Guid userId,
            IReadOnlyList<ScoredRecommendation> recommendations,
            MediaType mediaType)
        {
            if (recommendations == null)
            {
                throw new ArgumentNullException(nameof(recommendations));
            }

            // Get or create lock for this user
            var userLock = _userLocks.GetOrAdd(userId, _ => new object());

            lock (userLock)
            {
                return SyncRecommendationsInternal(userId, recommendations, mediaType);
            }
        }

        /// <summary>
        /// Checks if a file is a video file based on extension.
        /// </summary>
        /// <param name="filePath">Path to check.</param>
        /// <returns>True if the file has a video extension.</returns>
        private static bool IsVideoFile(string filePath)
        {
            var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
            };

            var extension = Path.GetExtension(filePath);
            return videoExtensions.Contains(extension);
        }

        private int SyncRecommendationsInternal(
            Guid userId,
            IReadOnlyList<ScoredRecommendation> recommendations,
            MediaType mediaType)
        {
            var libraryPath = GetUserLibraryPath(userId, mediaType);

            // Step 1: Clear all existing files/folders
            ClearRecommendationsInternal(userId, mediaType);

            // Step 2: Ensure directory exists (may have been deleted in clear)
            Directory.CreateDirectory(libraryPath);

            // Step 3: Create new files for all recommendations
            var createdCount = 0;
            foreach (var rec in recommendations)
            {
                try
                {
                    var item = _libraryManager.GetItemById(rec.ItemId);
                    if (item == null)
                    {
                        _logger.LogWarning("Item {ItemId} not found in library", rec.ItemId);
                        continue;
                    }

                    if (string.IsNullOrEmpty(item.Path))
                    {
                        _logger.LogWarning("Item {ItemId} ({ItemName}) has no path, skipping .strm creation", rec.ItemId, item.Name);
                        continue;
                    }

                    CreateStrmFile(libraryPath, item);
                    createdCount++;
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Failed to create .strm file for item {ItemId} (IO error)", rec.ItemId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogError(ex, "Failed to create .strm file for item {ItemId} (access denied)", rec.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create .strm file for item {ItemId}", rec.ItemId);
                }
            }

            _logger.LogInformation(
                "Updated {MediaType} recommendations for user {UserId}: {Created} files created",
                mediaType,
                userId,
                createdCount);

            return createdCount;
        }

        private void ClearRecommendationsInternal(Guid userId, MediaType mediaType)
        {
            var libraryPath = GetUserLibraryPath(userId, mediaType);

            if (!Directory.Exists(libraryPath))
            {
                return;
            }

            try
            {
                // Delete the entire directory and recreate it
                // This is cleaner than selective file deletion and helps prevent stale metadata
                _logger.LogDebug("Deleting entire virtual library directory: {Path}", libraryPath);
                Directory.Delete(libraryPath, recursive: true);

                _logger.LogInformation(
                    "Cleared all {MediaType} items for user {UserId} (deleted directory)",
                    mediaType,
                    userId);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete virtual library directory (IO error): {Path}", libraryPath);

                // Fallback to individual file/folder deletion
                FallbackClearRecommendations(userId, mediaType, libraryPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Failed to delete virtual library directory (access denied): {Path}", libraryPath);

                // Fallback to individual file/folder deletion
                FallbackClearRecommendations(userId, mediaType, libraryPath);
            }
        }

        private void FallbackClearRecommendations(Guid userId, MediaType mediaType, string libraryPath)
        {
            _logger.LogWarning("Falling back to individual file deletion for {Path}", libraryPath);

            var deletedCount = 0;

            // Delete all subfolders (movie folders for movies, series folders for TV)
            // Both movies and series now use folder structures
            var folders = GetExistingSeriesFolders(libraryPath);
            foreach (var folder in folders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    deletedCount++;
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Failed to delete folder (IO error): {Folder}", folder);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogError(ex, "Failed to delete folder (access denied): {Folder}", folder);
                }
            }

            // Delete any loose .strm files (stragglers from old format)
            var files = GetExistingStrmFiles(libraryPath);
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Failed to delete .strm file (IO error): {File}", file);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogError(ex, "Failed to delete .strm file (access denied): {File}", file);
                }
            }

            _logger.LogInformation(
                "Cleared {Count} {MediaType} items for user {UserId} (fallback method)",
                deletedCount,
                mediaType,
                userId);
        }

        private void CreateStrmFile(string libraryPath, BaseItem item)
        {
            // For TV series, create a folder structure with episode .strm files
            if (item is Series series)
            {
                CreateSeriesStrmStructure(libraryPath, series);
            }
            else
            {
                // For movies, create a folder with .strm, .nfo, and trailer files
                CreateMovieFolderStructure(libraryPath, item);
            }
        }

        private void CreateMovieFolderStructure(string libraryPath, BaseItem item)
        {
            // Create movie folder: "Movie Name (Year) [tmdbid-123]"
            var folderName = GenerateMovieFolderName(item);
            var movieFolderPath = Path.Combine(libraryPath, folderName);

            try
            {
                Directory.CreateDirectory(movieFolderPath);

                // Generate base filename (without extension)
                var baseFilename = folderName;

                // 1. Create the main .strm file
                var strmPath = Path.Combine(movieFolderPath, baseFilename + ".strm");
                File.WriteAllText(strmPath, item.Path ?? string.Empty);

                // 2. Create the .nfo file with metadata (runtime, etc.)
                var nfoPath = Path.Combine(movieFolderPath, baseFilename + ".nfo");
                var nfoContent = _nfoWriter.GenerateMovieNfo(item);
                File.WriteAllText(nfoPath, nfoContent);

                // 3. Create trailer .strm files if the source has local trailers
                var trailerCount = CreateTrailerStrmFiles(movieFolderPath, baseFilename, item);

                _logger.LogDebug(
                    "Created movie folder: {FolderName} with .strm, .nfo, and {TrailerCount} trailer(s)",
                    folderName,
                    trailerCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create movie folder structure for {MovieName}", item.Name);
                throw;
            }
        }

        /// <summary>
        /// Creates .strm files for local trailers using the -trailer suffix.
        /// </summary>
        /// <param name="movieFolderPath">Path to the movie folder.</param>
        /// <param name="baseFilename">Base filename for the movie (without extension).</param>
        /// <param name="item">The movie item.</param>
        /// <returns>Number of trailer files created.</returns>
        private int CreateTrailerStrmFiles(string movieFolderPath, string baseFilename, BaseItem item)
        {
            // Get local trailer paths for this item
            var trailerPaths = GetLocalTrailerPaths(item);
            if (trailerPaths.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < trailerPaths.Count; i++)
            {
                var trailerSourcePath = trailerPaths[i];

                // Use -trailer suffix for single trailer, -trailer1, -trailer2, etc. for multiple
                string trailerFilename;
                if (trailerPaths.Count == 1)
                {
                    trailerFilename = baseFilename + "-trailer.strm";
                }
                else
                {
                    trailerFilename = baseFilename + $"-trailer{i + 1}.strm";
                }

                var trailerStrmPath = Path.Combine(movieFolderPath, trailerFilename);
                File.WriteAllText(trailerStrmPath, trailerSourcePath);
            }

            return trailerPaths.Count;
        }

        /// <summary>
        /// Gets local trailer file paths for an item by searching the source directory.
        /// Jellyfin supports trailers in a 'trailers' subfolder or with -trailer suffix.
        /// </summary>
        /// <param name="item">The media item.</param>
        /// <returns>List of trailer file paths.</returns>
        private IReadOnlyList<string> GetLocalTrailerPaths(BaseItem item)
        {
            var trailerPaths = new List<string>();

            try
            {
                if (string.IsNullOrEmpty(item.Path))
                {
                    return trailerPaths;
                }

                var itemDirectory = Path.GetDirectoryName(item.Path);
                if (string.IsNullOrEmpty(itemDirectory) || !Directory.Exists(itemDirectory))
                {
                    return trailerPaths;
                }

                // Check for trailers subfolder
                var trailersFolder = Path.Combine(itemDirectory, "trailers");
                if (Directory.Exists(trailersFolder))
                {
                    var trailerFiles = Directory.GetFiles(trailersFolder, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => IsVideoFile(f))
                        .ToList();
                    trailerPaths.AddRange(trailerFiles);
                }

                // Check for files with -trailer suffix in the same directory
                var suffixPatterns = new[] { "-trailer", ".trailer", "_trailer" };
                var allFiles = Directory.GetFiles(itemDirectory, "*.*", SearchOption.TopDirectoryOnly);

                foreach (var file in allFiles)
                {
                    // Skip non-video files
                    if (!IsVideoFile(file))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileNameWithoutExtension(file);

                    // Check if file matches trailer suffix pattern
                    foreach (var suffix in suffixPatterns)
                    {
                        if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("trailer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!trailerPaths.Contains(file))
                            {
                                trailerPaths.Add(file);
                            }

                            break;
                        }
                    }
                }

                _logger.LogDebug("Found {Count} local trailers for {ItemName}", trailerPaths.Count, item.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get local trailers for item {ItemId}", item.Id);
            }

            return trailerPaths;
        }

        private string GenerateMovieFolderName(BaseItem item)
        {
            var title = SanitizeFilename(item.Name ?? "Unknown");
            var year = item.ProductionYear ?? 0;
            var providerId = GetProviderId(item);

            if (year > 0)
            {
                return $"{title} ({year}) [{providerId}]";
            }

            return $"{title} [{providerId}]";
        }

        private void CreateSeriesStrmStructure(string libraryPath, Series series)
        {
            // Create series folder: "Series Name (Year) [tvdbid-123]"
            var seriesFolderName = GenerateSeriesFolderName(series);
            var seriesPath = Path.Combine(libraryPath, seriesFolderName);

            try
            {
                Directory.CreateDirectory(seriesPath);

                // 1. Create tvshow.nfo with series metadata (runtime, etc.)
                var tvshowNfoPath = Path.Combine(seriesPath, "tvshow.nfo");
                var tvshowNfoContent = _nfoWriter.GenerateSeriesNfo(series);
                File.WriteAllText(tvshowNfoPath, tvshowNfoContent);

                // 2. Create trailer .strm files if the source has local trailers
                var trailerCount = CreateSeriesTrailerStrmFiles(seriesPath, seriesFolderName, series);

                // 3. Get all episodes for this series
                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = series.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    Recursive = true
                })
                .OfType<Episode>()
                .OrderBy(e => e.ParentIndexNumber ?? 0)
                .ThenBy(e => e.IndexNumber ?? 0)
                .ToList();

                if (episodes.Count == 0)
                {
                    _logger.LogWarning("Series {SeriesName} has no episodes, skipping .strm creation", series.Name);
                    return;
                }

                // Group episodes by season
                var episodesBySeason = episodes.GroupBy(e => e.ParentIndexNumber ?? 0);

                var episodeCount = 0;
                foreach (var seasonGroup in episodesBySeason.OrderBy(g => g.Key))
                {
                    var seasonNumber = seasonGroup.Key;
                    var seasonFolder = seasonNumber == 0 ? "Specials" : $"Season {seasonNumber:D2}";
                    var seasonPath = Path.Combine(seriesPath, seasonFolder);

                    Directory.CreateDirectory(seasonPath);

                    foreach (var episode in seasonGroup)
                    {
                        if (string.IsNullOrEmpty(episode.Path))
                        {
                            _logger.LogWarning("Episode {EpisodeName} has no path, skipping", episode.Name);
                            continue;
                        }

                        // Create episode .strm file
                        var episodeBaseFilename = GenerateEpisodeBaseFilename(episode);
                        var episodeStrmPath = Path.Combine(seasonPath, episodeBaseFilename + ".strm");
                        File.WriteAllText(episodeStrmPath, episode.Path);

                        // Create episode .nfo file with runtime
                        var episodeNfoPath = Path.Combine(seasonPath, episodeBaseFilename + ".nfo");
                        var episodeNfoContent = _nfoWriter.GenerateEpisodeNfo(episode);
                        File.WriteAllText(episodeNfoPath, episodeNfoContent);

                        episodeCount++;
                    }
                }

                _logger.LogDebug(
                    "Created series folder structure: {SeriesFolder} with {EpisodeCount} episodes, tvshow.nfo, and {TrailerCount} trailer(s)",
                    seriesFolderName,
                    episodeCount,
                    trailerCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create series folder structure for {SeriesName}", series.Name);
                throw;
            }
        }

        /// <summary>
        /// Creates .strm files for series-level local trailers using the -trailer suffix.
        /// </summary>
        /// <param name="seriesPath">Path to the series folder.</param>
        /// <param name="seriesFolderName">Series folder name (used as base filename).</param>
        /// <param name="series">The series item.</param>
        /// <returns>Number of trailer files created.</returns>
        private int CreateSeriesTrailerStrmFiles(string seriesPath, string seriesFolderName, Series series)
        {
            // Get local trailer paths for this series
            var trailerPaths = GetLocalTrailerPaths(series);
            if (trailerPaths.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < trailerPaths.Count; i++)
            {
                var trailerSourcePath = trailerPaths[i];

                // Use -trailer suffix for single trailer, -trailer1, -trailer2, etc. for multiple
                string trailerFilename;
                if (trailerPaths.Count == 1)
                {
                    trailerFilename = seriesFolderName + "-trailer.strm";
                }
                else
                {
                    trailerFilename = seriesFolderName + $"-trailer{i + 1}.strm";
                }

                var trailerStrmPath = Path.Combine(seriesPath, trailerFilename);
                File.WriteAllText(trailerStrmPath, trailerSourcePath);
            }

            return trailerPaths.Count;
        }

        /// <summary>
        /// Generates base filename for an episode (without extension).
        /// </summary>
        private string GenerateEpisodeBaseFilename(Episode episode)
        {
            var seriesName = SanitizeFilename(episode.SeriesName ?? "Unknown");
            var seasonNum = episode.ParentIndexNumber ?? 0;
            var episodeNum = episode.IndexNumber ?? 0;
            var episodeName = SanitizeFilename(episode.Name ?? "Episode");

            // Format: "SeriesName - S01E01 - Episode Title"
            return $"{seriesName} - S{seasonNum:D2}E{episodeNum:D2} - {episodeName}";
        }

        private string GenerateSeriesFolderName(Series series)
        {
            var title = SanitizeFilename(series.Name ?? "Unknown");
            var year = series.ProductionYear ?? 0;
            var providerId = GetProviderId(series);

            if (year > 0)
            {
                return $"{title} ({year}) [{providerId}]";
            }

            return $"{title} [{providerId}]";
        }

        private string GetProviderId(BaseItem item)
        {
            // ProviderIds should never be null but be defensive
            var providerIds = item.ProviderIds ?? new Dictionary<string, string>();

            // Try TMDB first (for movies)
            if (providerIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId))
            {
                return $"tmdbid-{tmdbId}";
            }

            // Try TVDB (for series)
            if (providerIds.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId))
            {
                return $"tvdbid-{tvdbId}";
            }

            // Fallback to Jellyfin internal ID
            return $"jellyfinid-{item.Id}";
        }

        private string SanitizeFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return "Unknown";
            }

            // Remove invalid filename characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", filename.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            // Prevent path traversal: remove directory separators and parent directory references
            sanitized = sanitized.Replace("..", "_")
                                 .Replace("/", "_")
                                 .Replace("\\", "_");

            // Additional safety: ensure no leading dots or dashes that could cause issues
            sanitized = sanitized.TrimStart('.', '-');

            // Limit length
            if (sanitized.Length > 200)
            {
                sanitized = sanitized.Substring(0, 200);
            }

            // Final check: if empty after sanitization, use default
            sanitized = sanitized.TrimEnd();
            if (string.IsNullOrEmpty(sanitized))
            {
                return "Unknown";
            }

            return sanitized;
        }

        private List<string> GetExistingStrmFiles(string libraryPath)
        {
            if (!Directory.Exists(libraryPath))
            {
                return new List<string>();
            }

            try
            {
                return Directory.GetFiles(libraryPath, "*.strm", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to enumerate .strm files in {Path}", libraryPath);
                return new List<string>();
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied when enumerating .strm files in {Path}", libraryPath);
                return new List<string>();
            }
        }

        private List<string> GetExistingSeriesFolders(string libraryPath)
        {
            if (!Directory.Exists(libraryPath))
            {
                return new List<string>();
            }

            try
            {
                return Directory.GetDirectories(libraryPath, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to enumerate series folders in {Path}", libraryPath);
                return new List<string>();
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied when enumerating series folders in {Path}", libraryPath);
                return new List<string>();
            }
        }
    }
}
