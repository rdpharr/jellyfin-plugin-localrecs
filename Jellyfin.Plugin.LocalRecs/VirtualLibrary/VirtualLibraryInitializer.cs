using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Initializes virtual library directories on startup and logs setup instructions.
    /// Creates per-user directories and provides detailed guidance for manual library setup.
    /// </summary>
    public class VirtualLibraryInitializer : IHostedService
    {
        private readonly ILogger<VirtualLibraryInitializer> _logger;
        private readonly IUserManager _userManager;
        private readonly string _virtualLibraryBasePath;
        private readonly VirtualLibraryManager _virtualLibraryManager;
        private readonly PlayStatusSyncService _playStatusSyncService;

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualLibraryInitializer"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager for accessing Jellyfin users.</param>
        /// <param name="virtualLibraryBasePath">Base path for virtual libraries.</param>
        /// <param name="virtualLibraryManager">Virtual library manager for directory operations.</param>
        /// <param name="playStatusSyncService">Play status sync service.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public VirtualLibraryInitializer(
            ILogger<VirtualLibraryInitializer> logger,
            IUserManager userManager,
            string virtualLibraryBasePath,
            VirtualLibraryManager virtualLibraryManager,
            PlayStatusSyncService playStatusSyncService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _virtualLibraryBasePath = virtualLibraryBasePath ?? throw new ArgumentNullException(nameof(virtualLibraryBasePath));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
            _playStatusSyncService = playStatusSyncService ?? throw new ArgumentNullException(nameof(playStatusSyncService));
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                InitializeDirectories(cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    LogSetupInstructions();
                    _logger.LogInformation("Play status sync service is active and monitoring virtual library changes");

                    // Sync play status from source library to virtual library items
                    // This ensures virtual library items reflect the real library's play status
                    _logger.LogInformation("Syncing play status from source library to virtual library items...");
                    _playStatusSyncService.SyncPlayStatusFromSourceLibraryForAllUsers();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Virtual library initialization was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize virtual libraries");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Dispose the play status sync service to unsubscribe from events and flush pending updates
            _playStatusSyncService.Dispose();
            _logger.LogInformation("Virtual library services stopped");
            return Task.CompletedTask;
        }

        private void InitializeDirectories(CancellationToken cancellationToken)
        {
            // Ensure base directory exists
            try
            {
                Directory.CreateDirectory(_virtualLibraryBasePath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to create virtual library base directory: {Path}", _virtualLibraryBasePath);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied creating virtual library base directory: {Path}", _virtualLibraryBasePath);
                return;
            }

            // Get users once to avoid multiple enumerations
            var users = _userManager.Users.ToList();
            var successCount = 0;

            foreach (var user in users)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (_virtualLibraryManager.EnsureUserDirectoriesExist(user.Id, user.Username))
                {
                    successCount++;
                }
            }

            _logger.LogInformation(
                "Initialized virtual library directories for {SuccessCount}/{TotalCount} users at {BasePath}",
                successCount,
                users.Count,
                _virtualLibraryBasePath);
        }

        private void LogSetupInstructions()
        {
            var users = _userManager.Users.ToList();

            if (users.Count == 0)
            {
                _logger.LogWarning("No users found - virtual libraries will be created when users are added");
                return;
            }

            var instructions = BuildSetupInstructions(users);

            // Log as info level with newlines for readability
            foreach (var line in instructions.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogInformation("{Line}", line);
                }
            }
        }

        private string BuildSetupInstructions(System.Collections.Generic.List<Jellyfin.Database.Implementations.Entities.User> users)
        {
            var sb = new StringBuilder();

            sb.AppendLine("================================================================================");
            sb.AppendLine($"Local Recommendations - Virtual Libraries Initialized (Build: {Plugin.BuildVersion})");
            sb.AppendLine("================================================================================");
            sb.AppendLine("IMPORTANT: Each content type is a SEPARATE library in Jellyfin");
            sb.AppendLine("(e.g., \"John's Recommended Movies\", \"John's Recommended TV\")");
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            foreach (var user in users)
            {
                var moviePath = Path.Combine(_virtualLibraryBasePath, user.Id.ToString(), "movies");
                var tvPath = Path.Combine(_virtualLibraryBasePath, user.Id.ToString(), "tv");
                var username = user.Username ?? "Unknown";

                sb.AppendLine($"User: {username} (ID: {user.Id})");
                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("  [1] Recommended Movies:");
                sb.AppendLine($"      Path: {moviePath}");
                sb.AppendLine("      Library Type: Movies");
                sb.AppendLine($"      Suggested Name: \"{username}'s Recommended Movies\"");
                sb.AppendLine();
                sb.AppendLine("  [2] Recommended TV:");
                sb.AppendLine($"      Path: {tvPath}");
                sb.AppendLine("      Library Type: Shows");
                sb.AppendLine($"      Suggested Name: \"{username}'s Recommended TV\"");
                sb.AppendLine();
                sb.AppendLine("  Setup Instructions:");
                sb.AppendLine("    1. Go to Jellyfin Dashboard → Libraries → Add Media Library");
                sb.AppendLine("    2. For EACH content type above, create a SEPARATE library:");
                sb.AppendLine("       - Select content type (Movies or Shows)");
                sb.AppendLine("       - Add the folder path shown above");
                sb.AppendLine("       - Use the suggested library name");
                sb.AppendLine("    3. Set library permissions:");
                sb.AppendLine($"       - Dashboard → Users → {username} → Library Access");
                sb.AppendLine($"       - Enable ONLY {username}'s recommendation libraries");
                sb.AppendLine("       - Disable other users' recommendation libraries");
                sb.AppendLine("--------------------------------------------------------------------------------");
            }

            sb.AppendLine("================================================================================");
            sb.AppendLine("Next Step: Dashboard → Scheduled Tasks → 'Refresh Local Recommendations' → Run");
            sb.AppendLine("See plugin configuration page for copy-paste friendly paths and instructions.");
            sb.AppendLine("================================================================================");

            return sb.ToString();
        }
    }
}
