using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
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
        /// Normalized base path with forward slashes and trailing separator.
        /// Cached for performance in path comparison operations.
        /// </summary>
        private readonly string _normalizedBasePath;

        /// <summary>
        /// Queue for debouncing play status updates.
        /// Key: (UserId, VirtualItemId), Value: (VirtualItem, UserData).
        /// </summary>
        private readonly ConcurrentDictionary<(Guid UserId, Guid VirtualItemId), (BaseItem VirtualItem, MediaBrowser.Controller.Entities.UserItemData UserData)> _updateQueue;

        /// <summary>
        /// Tracks in-progress sync operations to prevent re-entrancy.
        /// Key: (UserId, ItemId).
        /// </summary>
        private readonly ConcurrentDictionary<(Guid UserId, Guid ItemId), byte> _inProgressSyncs;

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
        private volatile bool _disposed;

        /// <summary>
        /// Flag to track if timer is currently active.
        /// </summary>
        private bool _timerActive;

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

            // Cache normalized base path for path comparison operations
            _normalizedBasePath = NormalizePath(virtualLibraryBasePath);

            // Initialize debounced queue
            _updateQueue = new ConcurrentDictionary<(Guid, Guid), (BaseItem, MediaBrowser.Controller.Entities.UserItemData)>();

            // Initialize re-entrancy protection
            _inProgressSyncs = new ConcurrentDictionary<(Guid, Guid), byte>();

            // Timer starts on-demand when items are added to queue
            _flushTimer = null;
        }

        /// <summary>
        /// Subscribe to user data change events and library item events.
        /// Call this during plugin initialization.
        /// </summary>
        public void Initialize()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _libraryManager.ItemAdded += OnLibraryItemAdded;
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
            _libraryManager.ItemAdded -= OnLibraryItemAdded;

            // Standard IDisposable pattern
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Syncs play status from source library items to virtual library items.
        /// This ensures virtual library items reflect the real library's play status.
        /// Should be called after virtual library items are created or scanned.
        /// </summary>
        /// <param name="userId">The user ID to sync for.</param>
        public void SyncPlayStatusFromSourceLibrary(Guid userId)
        {
            if (_disposed)
            {
                return;
            }

            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for play status sync", userId);
                return;
            }

            _logger.LogInformation("Syncing play status from source library for user {UserId}", userId);

            var syncedCount = 0;
            var errorCount = 0;

            try
            {
                // Find all virtual library items for this user
                var userVirtualLibraryPath = Path.Combine(_virtualLibraryBasePath, userId.ToString());
                if (!Directory.Exists(userVirtualLibraryPath))
                {
                    return;
                }

                // Find all .strm files in the user's virtual library
                var strmFiles = Directory.GetFiles(userVirtualLibraryPath, "*.strm", SearchOption.AllDirectories);

                foreach (var strmFile in strmFiles)
                {
                    try
                    {
                        // Read the source path from the .strm file
                        var sourcePath = File.ReadAllText(strmFile).Trim();
                        if (string.IsNullOrEmpty(sourcePath))
                        {
                            continue;
                        }

                        // Find the source and virtual items
                        var sourceItem = _libraryManager.FindByPath(sourcePath, isFolder: false);
                        var virtualItem = _libraryManager.FindByPath(strmFile, isFolder: false);

                        if (sourceItem == null || virtualItem == null)
                        {
                            continue;
                        }

                        // Sync from source to virtual
                        if (TrySyncUserData(user, sourceItem, virtualItem, userId))
                        {
                            syncedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogWarning(ex, "Failed to sync play status for .strm file: {Path}", strmFile);
                    }
                }

                _logger.LogInformation(
                    "Completed play status sync from source library for user {UserId}: {Synced} items synced, {Errors} errors",
                    userId,
                    syncedCount,
                    errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync play status from source library for user {UserId}", userId);
            }
        }

        /// <summary>
        /// Syncs play status from source library for all users.
        /// </summary>
        public void SyncPlayStatusFromSourceLibraryForAllUsers()
        {
            if (_disposed)
            {
                return;
            }

            _logger.LogInformation("Syncing play status from source library for all users");

            foreach (var user in _userManager.Users)
            {
                try
                {
                    SyncPlayStatusFromSourceLibrary(user.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync play status for user {UserId}", user.Id);
                }
            }

            _logger.LogInformation("Completed play status sync from source library for all users");
        }

        /// <summary>
        /// Normalizes a path for cross-platform comparison.
        /// Converts backslashes to forward slashes and ensures trailing separator.
        /// </summary>
        private static string NormalizePath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.EndsWith('/'))
            {
                normalized += '/';
            }

            return normalized;
        }

        /// <summary>
        /// Checks if source and target user data differ in any tracked fields.
        /// </summary>
        private static bool NeedsSync(
            MediaBrowser.Controller.Entities.UserItemData source,
            MediaBrowser.Controller.Entities.UserItemData target)
        {
            return source.Played != target.Played
                || source.PlaybackPositionTicks != target.PlaybackPositionTicks
                || source.PlayCount != target.PlayCount
                || source.LastPlayedDate != target.LastPlayedDate
                || source.IsFavorite != target.IsFavorite;
        }

        /// <summary>
        /// Checks if the given path is within the virtual library base path.
        /// </summary>
        private bool IsVirtualLibraryPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var normalizedPath = path.Replace('\\', '/');
            return normalizedPath.StartsWith(_normalizedBasePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if the given item is in the virtual library.
        /// </summary>
        private bool IsVirtualLibraryItem(BaseItem item)
        {
            return IsVirtualLibraryPath(item.Path);
        }

        /// <summary>
        /// Extracts the user ID from a virtual library item path.
        /// Path format: {basePath}/{userId}/{movies|tv}/{item}.
        /// </summary>
        private Guid? ExtractUserIdFromPath(string itemPath)
        {
            if (!IsVirtualLibraryPath(itemPath))
            {
                return null;
            }

            var normalizedPath = itemPath.Replace('\\', '/');

            // Get the relative path after the base path
            var relativePath = normalizedPath.Substring(_normalizedBasePath.Length);

            // The first segment should be the user ID
            var firstSlash = relativePath.IndexOf('/');
            var userIdString = firstSlash > 0 ? relativePath.Substring(0, firstSlash) : relativePath;

            if (Guid.TryParse(userIdString, out var userId))
            {
                return userId;
            }

            return null;
        }

        /// <summary>
        /// Syncs user data from source item to target item.
        /// Returns true if data was synced, false if no update was needed.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="sourceItem">The source item to copy data from.</param>
        /// <param name="targetItem">The target item to copy data to.</param>
        /// <param name="userId">The user ID for re-entrancy tracking.</param>
        /// <returns>True if data was synced, false otherwise.</returns>
        private bool TrySyncUserData(User user, BaseItem sourceItem, BaseItem targetItem, Guid userId)
        {
            var sourceUserData = _userDataManager.GetUserData(user, sourceItem);
            var targetUserData = _userDataManager.GetUserData(user, targetItem);

            if (sourceUserData == null || targetUserData == null)
            {
                return false;
            }

            // Check if there's anything to sync
            if (!NeedsSync(sourceUserData, targetUserData))
            {
                return false;
            }

            // Copy data from source to target
            targetUserData.Played = sourceUserData.Played;
            targetUserData.PlaybackPositionTicks = sourceUserData.PlaybackPositionTicks;
            targetUserData.PlayCount = sourceUserData.PlayCount;
            targetUserData.LastPlayedDate = sourceUserData.LastPlayedDate;
            targetUserData.IsFavorite = sourceUserData.IsFavorite;

            // Use a sync key to prevent re-entrancy from our own event handler
            var syncKey = (userId, targetItem.Id);
            if (!_inProgressSyncs.TryAdd(syncKey, 0))
            {
                return false;
            }

            try
            {
                _userDataManager.SaveUserData(
                    user,
                    targetItem,
                    targetUserData,
                    MediaBrowser.Model.Entities.UserDataSaveReason.UpdateUserData,
                    CancellationToken.None);

                return true;
            }
            finally
            {
                _inProgressSyncs.TryRemove(syncKey, out _);
            }
        }

        /// <summary>
        /// Handles library item added events.
        /// Syncs play status from source when a new virtual library item is scanned.
        /// </summary>
        private void OnLibraryItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var item = e.Item;
                if (item == null || string.IsNullOrEmpty(item.Path))
                {
                    return;
                }

                // Only process .strm files in our virtual library
                if (!item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!IsVirtualLibraryItem(item))
                {
                    return;
                }

                // Extract user ID from the path
                var userId = ExtractUserIdFromPath(item.Path);
                if (userId == null)
                {
                    return;
                }

                // Sync play status for this specific item
                SyncSingleItemFromSource(userId.Value, item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling item added event for {Path}", e.Item?.Path);
            }
        }

        /// <summary>
        /// Syncs play status for a single virtual library item from its source.
        /// </summary>
        private void SyncSingleItemFromSource(Guid userId, BaseItem virtualItem)
        {
            try
            {
                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    return;
                }

                // Read the source path from the .strm file
                var sourcePath = File.ReadAllText(virtualItem.Path).Trim();
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return;
                }

                // Find the source item
                var sourceItem = _libraryManager.FindByPath(sourcePath, isFolder: false);
                if (sourceItem == null)
                {
                    return;
                }

                // Sync from source to virtual
                TrySyncUserData(user, sourceItem, virtualItem, userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync play status for new virtual item: {Path}", virtualItem.Path);
            }
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
                    _timerActive = false;
                    return;
                }

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
                }
                else
                {
                    // Re-schedule timer for remaining items
                    _flushTimer?.Change(5000, Timeout.Infinite);
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

                // Check if this is a virtual library .strm file
                if (!virtualItem.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!IsVirtualLibraryItem(virtualItem))
                {
                    return;
                }

                var userId = e.UserId;
                var userData = e.UserData;

                // AUTO-REMOVAL: If user has started watching (any progress > 0), remove the recommendation
                if (userData.PlaybackPositionTicks > 0)
                {
                    _logger.LogInformation(
                        "User {UserId} started watching recommendation '{ItemName}', removing from virtual library",
                        userId,
                        virtualItem.Name);

                    RemoveVirtualLibraryItem(userId, virtualItem);
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
            var key = (userId, virtualItem.Id);
            _updateQueue[key] = (virtualItem, userData);
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
                }
            }
        }

        /// <summary>
        /// Removes a virtual library item (recommendation) when user starts watching it.
        /// For movies, deletes the .strm file. For TV series/episodes, deletes the entire series folder.
        /// </summary>
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

                        TriggerLibraryScan();
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
                        File.Delete(filePath);
                        _logger.LogInformation(
                            "Removed recommendation file for user {UserId}: {FileName}",
                            userId,
                            Path.GetFileName(filePath));

                        TriggerLibraryScan();
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to remove virtual library item for user {UserId}: {ItemName} (IO error)", userId, virtualItem.Name);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Failed to remove virtual library item for user {UserId}: {ItemName} (access denied)", userId, virtualItem.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove virtual library item for user {UserId}: {ItemName}", userId, virtualItem.Name);
            }
        }

        /// <summary>
        /// Finds the series root folder for a TV series or episode item.
        /// </summary>
        private string? FindSeriesFolderForItem(BaseItem item)
        {
            var itemPath = item.Path;
            if (string.IsNullOrEmpty(itemPath))
            {
                return null;
            }

            // For Series items, the Path property typically points to the series folder itself
            if (item is Series && Directory.Exists(itemPath))
            {
                return itemPath;
            }

            // For episodes, navigate up to find the series folder (direct child of /tv/)
            var dir = Path.GetDirectoryName(itemPath);

            while (!string.IsNullOrEmpty(dir))
            {
                var parentDir = Path.GetDirectoryName(dir);

                if (parentDir != null &&
                    Path.GetFileName(parentDir).Equals("tv", StringComparison.OrdinalIgnoreCase) &&
                    parentDir.StartsWith(_virtualLibraryBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }

                dir = parentDir;
            }

            return null;
        }

        /// <summary>
        /// Triggers a library scan to update Jellyfin's database after file deletion.
        /// </summary>
        private void TriggerLibraryScan()
        {
            try
            {
                _libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to trigger library scan. Jellyfin will clean up on next scheduled scan.");
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

                // Prevent re-entrancy
                var syncKey = (userId, sourceItem.Id);
                if (!_inProgressSyncs.TryAdd(syncKey, 0))
                {
                    return;
                }

                try
                {
                    var sourceUserData = _userDataManager.GetUserData(user, sourceItem);
                    if (sourceUserData == null)
                    {
                        _logger.LogWarning("Could not get user data for source item: {Path}", sourcePath);
                        return;
                    }

                    // Check if sync is needed
                    if (!NeedsSync(virtualUserData, sourceUserData))
                    {
                        return;
                    }

                    // Sync from virtual to source
                    sourceUserData.Played = virtualUserData.Played;
                    sourceUserData.PlaybackPositionTicks = virtualUserData.PlaybackPositionTicks;
                    sourceUserData.PlayCount = virtualUserData.PlayCount;
                    sourceUserData.LastPlayedDate = virtualUserData.LastPlayedDate;
                    sourceUserData.IsFavorite = virtualUserData.IsFavorite;

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
                finally
                {
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
