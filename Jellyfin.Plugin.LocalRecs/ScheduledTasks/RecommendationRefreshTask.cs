using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.ScheduledTasks
{
    /// <summary>
    /// Scheduled task for refreshing recommendations for all users.
    /// Can be triggered manually or scheduled via Jellyfin's task scheduler.
    /// </summary>
    public class RecommendationRefreshTask : IScheduledTask
    {
        private readonly ILogger<RecommendationRefreshTask> _logger;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly RecommendationRefreshService _refreshService;
        private readonly VirtualLibraryManager _virtualLibraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationRefreshTask"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="refreshService">Recommendation refresh service.</param>
        /// <param name="virtualLibraryManager">Virtual library manager.</param>
        public RecommendationRefreshTask(
            ILogger<RecommendationRefreshTask> logger,
            IUserManager userManager,
            ILibraryManager libraryManager,
            RecommendationRefreshService refreshService,
            VirtualLibraryManager virtualLibraryManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
        }

        /// <inheritdoc />
        public string Name => "Refresh Local Recommendations";

        /// <inheritdoc />
        public string Key => "LocalRecsRefresh";

        /// <inheritdoc />
        public string Description => "Updates personalized recommendations for all users based on watch history and metadata similarity";

        /// <inheritdoc />
        public string Category => "Local Recommendations";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting recommendation refresh task (Build: {BuildVersion})", Plugin.BuildVersion);
            var startTime = DateTime.UtcNow;

            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

                // Step 1: Get all users (5% progress)
                progress?.Report(0);
                cancellationToken.ThrowIfCancellationRequested();
                var users = _userManager.Users.ToList();
                _logger.LogInformation("Generating recommendations for {UserCount} users", users.Count);
                progress?.Report(5);

                if (users.Count == 0)
                {
                    _logger.LogWarning("No users found, skipping recommendation refresh");
                    progress?.Report(100);
                    return;
                }

                // Step 2: Generate recommendations for all users (5-80% progress)
                var userIds = users.Select(u => u.Id).ToList();
                var userRecommendations = await _refreshService.GenerateRecommendationsForMultipleUsersAsync(
                    userIds,
                    config).ConfigureAwait(false);

                progress?.Report(80);

                // Step 3: Sync .strm files for each user (80-90% progress)
                cancellationToken.ThrowIfCancellationRequested();
                var successfulUsers = 0;
                var failedUsers = new List<string>();

                foreach (var user in users)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (userRecommendations.TryGetValue(user.Id, out var recs))
                        {
                            _logger.LogDebug("Syncing .strm files for user {UserName} ({UserId})", user.Username, user.Id);

                            // Update virtual library files
                            _virtualLibraryManager.SyncRecommendations(
                                user.Id,
                                recs.Movies,
                                MediaType.Movie);

                            _virtualLibraryManager.SyncRecommendations(
                                user.Id,
                                recs.Tv,
                                MediaType.Series);

                            successfulUsers++;
                            _logger.LogDebug(
                                "Successfully updated .strm files for {UserName}: {MovieCount} movies, {TvCount} TV shows",
                                user.Username,
                                recs.Movies.Count,
                                recs.Tv.Count);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to sync .strm files for user {UserName} ({UserId})", user.Username, user.Id);
                        failedUsers.Add(user.Username);
                    }
                }

                progress?.Report(90);

                // Step 4: Wait for file system to flush, then trigger library scan (90-95% progress)
                _logger.LogInformation("Waiting for file system flush before triggering library scan");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Triggering library scan for virtual recommendation libraries");

                LogLibraryScanInstructions();

                progress?.Report(95);

                // Step 5: Report results (100% progress)
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Recommendation refresh completed in {Duration:F2} seconds: {Success}/{Total} users successful",
                    duration.TotalSeconds,
                    successfulUsers,
                    users.Count);

                if (failedUsers.Count > 0)
                {
                    _logger.LogWarning("Failed to sync recommendations for {Count} users: {Users}", failedUsers.Count, string.Join(", ", failedUsers));
                }

                progress?.Report(100);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Recommendation refresh task was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recommendation refresh task failed");
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Daily execution at 4:00 AM by default
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = MediaBrowser.Model.Tasks.TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
                }
            };
        }

        private void LogLibraryScanInstructions()
        {
            // DON'T trigger automatic library scan - let user manually scan or wait for scheduled scan
            // The issue is that ValidateMediaLibrary() removes .strm files before metadata is fetched
            // Manual scan or scheduled scan works correctly because Jellyfin has time to process the files
            _logger.LogInformation("Virtual library files updated successfully");
            _logger.LogInformation("IMPORTANT: Please manually scan the recommendation libraries via:");
            _logger.LogInformation("  Dashboard → Libraries → [Your Recommendation Library] → Scan Library");
            _logger.LogInformation("  OR wait for the next scheduled library scan");
            _logger.LogInformation("Automatic scanning is disabled to prevent .strm file removal issues");
        }
    }
}
