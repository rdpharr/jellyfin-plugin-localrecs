using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Service for syncing play status between virtual library items and source library items.
    /// Ensures that when users watch or mark items in virtual recommendation libraries,
    /// the play status is reflected in the source library.
    /// Uses a debounced queue to prevent SQLite constraint violations during concurrent operations.
    /// </summary>
    public class PlayStatusSyncService : IDisposable
    {
        private readonly ILogger<PlayStatusSyncService> _logger;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly string _virtualLibraryBasePath;

        /// <summary>
        /// Queue for debouncing play status updates.
        /// Key: (UserId, VirtualItemId), Value: (VirtualItem, UserData).
        /// </summary>
        private readonly ConcurrentDictionary<(Guid UserId, Guid VirtualItemId), (BaseItem VirtualItem, MediaBrowser.Controller.Entities.UserItemData UserData)> _updateQueue;

        /// <summary>
        /// Tracks in-progress sync operations to prevent re-entrancy.
        /// Key: (UserId, SourceItemId).
        /// </summary>
        private readonly ConcurrentDictionary<(Guid UserId, Guid SourceItemId), byte> _inProgressSyncs;

        /// <summary>
        /// Lock for thread-safe queue flushing.
        /// </summary>
        private readonly object _flushLock = new object();

        /// <summary>
        /// Timer for flushing the update queue.
        /// Only active when queue has items.
        /// </summary>
        private Timer? _flushTimer;

        /// <summary>
        /// Flag to track if the service is disposed.
        /// Thread-safe flag accessed from multiple threads (event handlers, timer callbacks, dispose).
        /// </summary>
        private volatile bool _disposed = false;

        /// <summary>
        /// Flag to track if timer is currently active.
        /// </summary>
        private bool _timerActive = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayStatusSyncService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="virtualLibraryBasePath">Base path for virtual libraries.</param>
        public PlayStatusSyncService(
            ILogger<PlayStatusSyncService> logger,
            IUserDataManager userDataManager,
            ILibraryManager libraryManager,
            IUserManager userManager,
            string virtualLibraryBasePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _virtualLibraryBasePath = virtualLibraryBasePath ?? throw new ArgumentNullException(nameof(virtualLibraryBasePath));

            // Initialize debounced queue
            _updateQueue = new ConcurrentDictionary<(Guid, Guid), (BaseItem, MediaBrowser.Controller.Entities.UserItemData)>();

            // Initialize re-entrancy protection
            _inProgressSyncs = new ConcurrentDictionary<(Guid, Guid), byte>();

            // Timer starts on-demand when items are added to queue
            _flushTimer = null;
        }

        /// <summary>
        /// Subscribe to user data change events.
        /// Call this during plugin initialization.
        /// </summary>
        public void Initialize()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _logger.LogInformation("Play status sync service initialized");
        }

        /// <summary>
        /// Unsubscribe from events and flush remaining updates.
        /// Call this during plugin disposal.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // Prevent new operations
            _disposed = true;

            // Dispose timer and wait for any active callback to complete
            // The timer's Dispose(WaitHandle) method blocks until the callback finishes
            if (_flushTimer != null)
            {
                using (var waitHandle = new ManualResetEvent(false))
                {
                    _flushTimer.Dispose(waitHandle);

                    // Wait up to 10 seconds for the callback to complete
                    if (!waitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        _logger.LogWarning("Timer callback did not complete within timeout during disposal");
                    }
                }
            }

            // Now safe to flush remaining updates (callback is definitely not running)
            FlushQueue();

            // Unsubscribe from events
            _userDataManager.UserDataSaved -= OnUserDataSaved;

            _logger.LogDebug("Play status sync service disposed");

            // Standard IDisposable pattern
            GC.SuppressFinalize(this);
        }

        private void FlushQueueCallback(object? state)
        {
            FlushQueue();
        }

        private void FlushQueue()
        {
            if (_disposed)
            {
                return;
            }

            lock (_flushLock)
            {
                if (_updateQueue.IsEmpty)
                {
                    // Stop timer when queue is empty
                    _timerActive = false;
                    _logger.LogDebug("Queue empty, stopping debounce timer");
                    return;
                }

                _logger.LogDebug("Flushing {Count} queued play status updates", _updateQueue.Count);

                var successCount = 0;
                var failCount = 0;

                // Process all queued updates
                foreach (var kvp in _updateQueue)
                {
                    var (userId, virtualItemId) = kvp.Key;
                    var (virtualItem, userData) = kvp.Value;

                    try
                    {
                        // Remove from queue first (before processing to avoid retry loops)
                        _updateQueue.TryRemove(kvp.Key, out _);

                        // Sync to source immediately
                        SyncPlayStatusToSource(userId, virtualItem, userData);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to flush play status update for user {UserId}, item {ItemId}", userId, virtualItemId);
                        failCount++;
                    }
                }

                if (successCount > 0 || failCount > 0)
                {
                    _logger.LogInformation(
                        "Flushed play status updates: {Success} succeeded, {Failed} failed",
                        successCount,
                        failCount);
                }

                // Check if more items were added during flush
                if (_updateQueue.IsEmpty)
                {
                    _timerActive = false;
                    _logger.LogDebug("Queue now empty, stopping debounce timer");
                }
                else
                {
                    // Re-schedule timer for remaining items
                    _flushTimer?.Change(5000, Timeout.Infinite);
                    _logger.LogDebug("Re-scheduling timer for {Count} remaining items", _updateQueue.Count);
                }
            }
        }

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            try
            {
                var virtualItem = e.Item;
                if (virtualItem == null || string.IsNullOrEmpty(virtualItem.Path))
                {
                    return;
                }

                // Check if this is a virtual library item
                if (!IsVirtualLibraryItem(virtualItem))
                {
                    return;
                }

                // Only process if the item is a .strm file
                if (!virtualItem.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var userId = e.UserId;
                var userData = e.UserData;

                // AUTO-REMOVAL: If user has started watching (any progress > 0), remove the recommendation
                // The recommendation has served its purpose - user discovered the content
                if (userData.PlaybackPositionTicks > 0)
                {
                    _logger.LogInformation(
                        "User {UserId} started watching recommendation '{ItemName}', removing from virtual library",
                        userId,
                        virtualItem.Name);

                    RemoveVirtualLibraryItem(userId, virtualItem);

                    // Don't sync to source - no need to duplicate watch status
                    return;
                }

                // If no playback progress, sync normally (for favorites, etc.)
                QueuePlayStatusUpdate(userId, virtualItem, userData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process user data update for item {ItemId}", e.Item?.Id);
            }
        }

        private void QueuePlayStatusUpdate(Guid userId, BaseItem virtualItem, MediaBrowser.Controller.Entities.UserItemData userData)
        {
            // Add or update the queue entry (overwrites if already exists)
            var key = (userId, virtualItem.Id);
            _updateQueue[key] = (virtualItem, userData);

            _logger.LogDebug("Queued play status update for item {ItemName} (user: {UserId})", virtualItem.Name, userId);

            // Start timer if not already running
            EnsureTimerStarted();
        }

        private void EnsureTimerStarted()
        {
            if (_disposed)
            {
                return;
            }

            lock (_flushLock)
            {
                if (!_timerActive && !_disposed)
                {
                    if (_flushTimer == null)
                    {
                        _flushTimer = new Timer(FlushQueueCallback, null, 5000, Timeout.Infinite);
                    }
                    else
                    {
                        _flushTimer.Change(5000, Timeout.Infinite);
                    }

                    _timerActive = true;
                    _logger.LogDebug("Started debounce timer for play status updates");
                }
            }
        }

        private bool IsVirtualLibraryItem(BaseItem item)
        {
            var itemPath = item.Path;
            if (string.IsNullOrEmpty(itemPath))
            {
                return false;
            }

            // Use StartsWith to ensure the path is within the virtual library directory
            // Check both forward and back slashes for cross-platform compatibility
            var normalizedItemPath = itemPath.Replace('\\', '/');
            var normalizedBasePath = _virtualLibraryBasePath.Replace('\\', '/');

            // Ensure base path ends with separator to prevent false matches
            if (!normalizedBasePath.EndsWith('/'))
            {
                normalizedBasePath += '/';
            }

            return normalizedItemPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes a virtual library item (recommendation) when user starts watching it.
        /// For movies, deletes the .strm file. For TV series/episodes, deletes the entire series folder.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <param name="virtualItem">The virtual library item to remove.</param>
        private void RemoveVirtualLibraryItem(Guid userId, BaseItem virtualItem)
        {
            try
            {
                // For TV series or episodes, delete the entire series folder
                if (virtualItem is Series || virtualItem is Episode)
                {
                    var seriesFolder = FindSeriesFolderForItem(virtualItem);
                    if (seriesFolder != null && Directory.Exists(seriesFolder))
                    {
                        Directory.Delete(seriesFolder, recursive: true);
                        _logger.LogInformation(
                            "Removed recommendation series folder for user {UserId}: {Folder}",
                            userId,
                            Path.GetFileName(seriesFolder));

                        // Trigger library scan to update Jellyfin's database
                        TriggerLibraryScanForPath(seriesFolder);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Could not find series folder for item {ItemName} (user: {UserId})",
                            virtualItem.Name,
                            userId);
                    }
                }
                else
                {
                    // For movies, delete the .strm file
                    var filePath = virtualItem.Path;
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        var fileName = Path.GetFileName(filePath);
                        File.Delete(filePath);
                        _logger.LogInformation(
                            "Removed recommendation file for user {UserId}: {FileName}",
                            userId,
                            fileName);

                        // Trigger library scan to update Jellyfin's database
                        TriggerLibraryScanForPath(filePath);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove virtual library item for user {UserId}: {ItemName} (IO error)",
                    userId,
                    virtualItem.Name);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove virtual library item for user {UserId}: {ItemName} (access denied)",
                    userId,
                    virtualItem.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove virtual library item for user {UserId}: {ItemName}",
                    userId,
                    virtualItem.Name);
            }
        }

        /// <summary>
        /// Finds the series root folder for a TV series or episode item.
        /// Virtual library structure: {virtualLibraryBasePath}/{userId}/tv/{SeriesName}/Season XX/{episode.strm}.
        /// </summary>
        /// <param name="item">The series or episode item.</param>
        /// <returns>Path to series folder, or null if not found.</returns>
        private string? FindSeriesFolderForItem(BaseItem item)
        {
            var itemPath = item.Path;
            if (string.IsNullOrEmpty(itemPath))
            {
                return null;
            }

            // For Series items, the Path property typically points to the series folder itself.
            // Verify it's a directory that exists before returning.
            if (item is Series && Directory.Exists(itemPath))
            {
                return itemPath;
            }

            // For episodes (.strm files), navigate up the directory tree to find the series root folder.
            // The series folder is the direct child of the /tv/ directory within the virtual library structure.
            var dir = Path.GetDirectoryName(itemPath);

            while (!string.IsNullOrEmpty(dir))
            {
                var parentDir = Path.GetDirectoryName(dir);

                // Check if parent directory is the /tv/ folder within our virtual library
                // Use Path.GetFileName to get the directory name for cross-platform compatibility
                if (parentDir != null &&
                    Path.GetFileName(parentDir).Equals("tv", StringComparison.OrdinalIgnoreCase) &&
                    parentDir.StartsWith(_virtualLibraryBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Found the series folder (direct child of /tv/)
                    return dir;
                }

                dir = parentDir;
            }

            return null;
        }

        /// <summary>
        /// Triggers a library scan to update Jellyfin's database after file deletion.
        /// This ensures the removed item is cleaned up from the database.
        /// </summary>
        /// <param name="itemPath">Path to the removed item (for logging purposes).</param>
        private void TriggerLibraryScanForPath(string itemPath)
        {
            try
            {
                // Trigger a full library validation to clean up the deleted item from Jellyfin's database.
                // We don't check if the path exists first since we just deleted it.
                _libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
                _logger.LogDebug("Triggered library scan after removing virtual item: {Path}", itemPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to trigger library scan after removing virtual item: {Path}. Jellyfin will clean up on next scheduled scan.",
                    itemPath);
            }
        }

        private void SyncPlayStatusToSource(Guid userId, BaseItem virtualItem, MediaBrowser.Controller.Entities.UserItemData virtualUserData)
        {
            try
            {
                // Read the .strm file to get the source path
                var sourcePath = File.ReadAllText(virtualItem.Path).Trim();
                if (string.IsNullOrEmpty(sourcePath))
                {
                    _logger.LogWarning("Empty .strm file: {Path}", virtualItem.Path);
                    return;
                }

                // Find the source item by path
                var sourceItem = _libraryManager.FindByPath(sourcePath, isFolder: false);
                if (sourceItem == null)
                {
                    _logger.LogWarning("Source item not found for path: {Path}", sourcePath);
                    return;
                }

                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return;
                }

                // Prevent re-entrancy: check if we're already syncing this source item
                var syncKey = (userId, sourceItem.Id);
                if (!_inProgressSyncs.TryAdd(syncKey, 0))
                {
                    _logger.LogDebug("Skipping sync for {ItemName} - already in progress", sourceItem.Name);
                    return;
                }

                try
                {
                    // Get source item's current user data
                    var sourceUserData = _userDataManager.GetUserData(user, sourceItem);
                    if (sourceUserData == null)
                    {
                        _logger.LogWarning("Could not get user data for source item: {Path}", sourcePath);
                        return;
                    }

                    // Sync play status from virtual to source
                    bool needsUpdate = false;

                    if (virtualUserData.Played != sourceUserData.Played)
                    {
                        sourceUserData.Played = virtualUserData.Played;
                        needsUpdate = true;
                    }

                    if (virtualUserData.PlaybackPositionTicks != sourceUserData.PlaybackPositionTicks)
                    {
                        sourceUserData.PlaybackPositionTicks = virtualUserData.PlaybackPositionTicks;
                        needsUpdate = true;
                    }

                    if (virtualUserData.PlayCount != sourceUserData.PlayCount)
                    {
                        sourceUserData.PlayCount = virtualUserData.PlayCount;
                        needsUpdate = true;
                    }

                    if (virtualUserData.LastPlayedDate != sourceUserData.LastPlayedDate)
                    {
                        sourceUserData.LastPlayedDate = virtualUserData.LastPlayedDate;
                        needsUpdate = true;
                    }

                    if (virtualUserData.IsFavorite != sourceUserData.IsFavorite)
                    {
                        sourceUserData.IsFavorite = virtualUserData.IsFavorite;
                        needsUpdate = true;
                    }

                    if (needsUpdate)
                    {
                        _userDataManager.SaveUserData(
                            user,
                            sourceItem,
                            sourceUserData,
                            MediaBrowser.Model.Entities.UserDataSaveReason.UpdateUserData,
                            CancellationToken.None);

                        _logger.LogInformation(
                            "Synced play status from virtual item '{VirtualName}' to source item '{SourceName}' for user {UserId}",
                            virtualItem.Name,
                            sourceItem.Name,
                            userId);
                    }
                }
                finally
                {
                    // Always remove from in-progress set
                    _inProgressSyncs.TryRemove(syncKey, out _);
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to read .strm file: {Path}", virtualItem.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync play status to source for virtual item: {Path}", virtualItem.Path);
            }
        }
    }
}
