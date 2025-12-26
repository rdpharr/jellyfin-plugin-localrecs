using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Controller for retrieving virtual library paths for setup.
    /// </summary>
    [ApiController]
    [Route("LocalRecs")]
    [Authorize(Policy = "RequiresElevation")]
    public class LibraryPathsController : ControllerBase
    {
        private readonly ILogger<LibraryPathsController> _logger;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly VirtualLibraryManager _virtualLibraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryPathsController"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager instance.</param>
        /// <param name="libraryManager">Library manager instance.</param>
        /// <param name="virtualLibraryManager">Virtual library manager instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public LibraryPathsController(
            ILogger<LibraryPathsController> logger,
            IUserManager userManager,
            ILibraryManager libraryManager,
            VirtualLibraryManager virtualLibraryManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
        }

        /// <summary>
        /// Gets library paths for all users.
        /// </summary>
        /// <returns>List of user library path information.</returns>
        [HttpGet("Setup/Paths")]
        public ActionResult<List<UserLibraryPathInfo>> GetAllUserPaths()
        {
            try
            {
                var users = _userManager.Users.ToList();
                var paths = new List<UserLibraryPathInfo>();

                foreach (var user in users)
                {
                    var username = user.Username ?? "Unknown";
                    var moviePath = _virtualLibraryManager.GetUserLibraryPath(user.Id, MediaType.Movie);
                    var tvPath = _virtualLibraryManager.GetUserLibraryPath(user.Id, MediaType.Series);

                    // Check if both libraries exist by looking for virtual folders
                    var virtualFolders = _libraryManager.GetVirtualFolders();
                    bool movieLibraryExists = virtualFolders.Any(vf => vf.Locations.Any(loc =>
                        loc.Equals(moviePath, StringComparison.OrdinalIgnoreCase)));
                    bool tvLibraryExists = virtualFolders.Any(vf => vf.Locations.Any(loc =>
                        loc.Equals(tvPath, StringComparison.OrdinalIgnoreCase)));
                    bool librariesCreated = movieLibraryExists && tvLibraryExists;

                    paths.Add(new UserLibraryPathInfo
                    {
                        UserId = user.Id,
                        Username = username,
                        MovieLibraryPath = moviePath,
                        TvLibraryPath = tvPath,
                        SuggestedMovieLibraryName = $"{username}'s Recommended Movies",
                        SuggestedTvLibraryName = $"{username}'s Recommended TV",
                        LibrariesCreated = librariesCreated
                    });
                }

                return Ok(paths);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get library paths");
                return StatusCode(500, "Failed to retrieve library paths");
            }
        }

        /// <summary>
        /// Gets the latest benchmark results.
        /// </summary>
        /// <returns>Benchmark results as plain text.</returns>
        [HttpGet("Benchmark/Results")]
        public ActionResult<string> GetBenchmarkResults()
        {
            try
            {
                var pluginDataPath = Plugin.Instance?.DataFolderPath;
                if (string.IsNullOrEmpty(pluginDataPath))
                {
                    return NotFound("Plugin data path not available");
                }

                var resultsPath = Path.Combine(pluginDataPath, "benchmark_results.txt");
                if (!System.IO.File.Exists(resultsPath))
                {
                    return NotFound("No benchmark results found. Run the benchmark task first.");
                }

                var results = System.IO.File.ReadAllText(resultsPath);
                return Ok(new { results, lastModified = System.IO.File.GetLastWriteTimeUtc(resultsPath) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get benchmark results");
                return StatusCode(500, "Failed to retrieve benchmark results");
            }
        }

        /// <summary>
        /// Downloads the benchmark results as a text file.
        /// </summary>
        /// <returns>Benchmark results file.</returns>
        [HttpGet("Benchmark/Download")]
        public ActionResult DownloadBenchmarkResults()
        {
            try
            {
                var pluginDataPath = Plugin.Instance?.DataFolderPath;
                if (string.IsNullOrEmpty(pluginDataPath))
                {
                    return NotFound("Plugin data path not available");
                }

                var resultsPath = Path.Combine(pluginDataPath, "benchmark_results.txt");
                if (!System.IO.File.Exists(resultsPath))
                {
                    return NotFound("No benchmark results found. Run the benchmark task first.");
                }

                var fileBytes = System.IO.File.ReadAllBytes(resultsPath);
                var fileName = $"benchmark_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
                return File(fileBytes, "text/plain", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download benchmark results");
                return StatusCode(500, "Failed to download benchmark results");
            }
        }
    }
}
