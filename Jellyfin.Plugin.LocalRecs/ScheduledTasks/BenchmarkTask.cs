using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.ScheduledTasks
{
    /// <summary>
    /// Scheduled task for benchmarking recommendation engine performance.
    /// Measures vocabulary build, embedding computation, user profile generation, and recommendation scoring times.
    /// </summary>
    public class BenchmarkTask : IScheduledTask
    {
        private readonly ILogger<BenchmarkTask> _logger;
        private readonly IUserManager _userManager;
        private readonly LibraryAnalysisService _libraryAnalysisService;
        private readonly VocabularyBuilder _vocabularyBuilder;
        private readonly EmbeddingService _embeddingService;
        private readonly UserProfileService _userProfileService;
        private readonly RecommendationEngine _recommendationEngine;

        /// <summary>
        /// Initializes a new instance of the <see cref="BenchmarkTask"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="libraryAnalysisService">Library analysis service.</param>
        /// <param name="vocabularyBuilder">Vocabulary builder.</param>
        /// <param name="embeddingService">Embedding service.</param>
        /// <param name="userProfileService">User profile service.</param>
        /// <param name="recommendationEngine">Recommendation engine.</param>
        public BenchmarkTask(
            ILogger<BenchmarkTask> logger,
            IUserManager userManager,
            LibraryAnalysisService libraryAnalysisService,
            VocabularyBuilder vocabularyBuilder,
            EmbeddingService embeddingService,
            UserProfileService userProfileService,
            RecommendationEngine recommendationEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryAnalysisService = libraryAnalysisService ?? throw new ArgumentNullException(nameof(libraryAnalysisService));
            _vocabularyBuilder = vocabularyBuilder ?? throw new ArgumentNullException(nameof(vocabularyBuilder));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _userProfileService = userProfileService ?? throw new ArgumentNullException(nameof(userProfileService));
            _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        }

        /// <inheritdoc />
        public string Name => "Benchmark Recommendation Engine";

        /// <inheritdoc />
        public string Key => "LocalRecsBenchmark";

        /// <inheritdoc />
        public string Description => "Measures performance metrics for the recommendation engine (vocabulary build, embeddings, scoring)";

        /// <inheritdoc />
        public string Category => "Local Recommendations";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting recommendation engine benchmark");
            var results = new StringBuilder();
            var overallStopwatch = Stopwatch.StartNew();

            // Capture initial memory before benchmark
            var initialMemory = GC.GetTotalMemory(forceFullCollection: false);
            long peakMemoryBytes = initialMemory;

            results.AppendLine("================================================================================");
            results.AppendLine("Local Recommendations Benchmark Results");
            results.AppendLine(string.Format("Timestamp: {0:yyyy-MM-dd HH:mm:ss} UTC", DateTime.UtcNow));
            results.AppendLine("================================================================================");
            results.AppendLine();

            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

                // Step 1: Benchmark Library Analysis (10% progress)
                progress?.Report(0);
                cancellationToken.ThrowIfCancellationRequested();

                results.AppendLine("1. Library Analysis");
                results.AppendLine("   " + new string('-', 76));

                var libraryStopwatch = Stopwatch.StartNew();
                var library = await Task.Run(
                    () => _libraryAnalysisService.GetAllMediaItems(),
                    cancellationToken).ConfigureAwait(false);
                libraryStopwatch.Stop();

                var movieCount = library.Count(m => m.Type == MediaType.Movie);
                var seriesCount = library.Count(m => m.Type == MediaType.Series);

                results.AppendLine(string.Format("   Total Items:      {0:N0} ({1:N0} movies, {2:N0} series)", library.Count, movieCount, seriesCount));
                results.AppendLine(string.Format("   Analysis Time:    {0:N0} ms", libraryStopwatch.ElapsedMilliseconds));
                results.AppendLine();

                UpdatePeakMemory(ref peakMemoryBytes);
                progress?.Report(10);

                if (library.Count == 0)
                {
                    results.AppendLine("   WARNING: No items found in library. Cannot continue benchmark.");
                    LogAndSaveResults(results.ToString());
                    progress?.Report(100);
                    return;
                }

                // Step 2: Benchmark Vocabulary Building (30% progress)
                cancellationToken.ThrowIfCancellationRequested();
                results.AppendLine("2. Vocabulary Building");
                results.AppendLine("   " + new string('-', 76));

                var vocabStopwatch = Stopwatch.StartNew();
                var vocabulary = await Task.Run(
                    () => _vocabularyBuilder.BuildVocabulary(
                        library,
                        config.MaxVocabularyActors,
                        config.MaxVocabularyDirectors,
                        config.MaxVocabularyTags),
                    cancellationToken).ConfigureAwait(false);
                vocabStopwatch.Stop();

                results.AppendLine(string.Format("   Genres:           {0:N0}", vocabulary.Genres.Count));
                results.AppendLine(string.Format("   Actors:           {0:N0}", vocabulary.Actors.Count));
                results.AppendLine(string.Format("   Directors:        {0:N0}", vocabulary.Directors.Count));
                results.AppendLine(string.Format("   Tags:             {0:N0}", vocabulary.Tags.Count));
                results.AppendLine(string.Format("   Total Features:   {0:N0}", vocabulary.TotalFeatures));
                results.AppendLine(string.Format("   Build Time:       {0:N0} ms", vocabStopwatch.ElapsedMilliseconds));
                results.AppendLine();

                UpdatePeakMemory(ref peakMemoryBytes);
                progress?.Report(30);

                // Step 3: Benchmark Embedding Computation (60% progress)
                cancellationToken.ThrowIfCancellationRequested();
                results.AppendLine("3. Embedding Computation");
                results.AppendLine("   " + new string('-', 76));

                var embeddingStopwatch = Stopwatch.StartNew();
                var embeddings = await Task.Run(
                    () => _embeddingService.ComputeEmbeddings(library, vocabulary),
                    cancellationToken).ConfigureAwait(false);
                embeddingStopwatch.Stop();

                var avgTimePerItem = library.Count > 0 ? (double)embeddingStopwatch.ElapsedMilliseconds / library.Count : 0;

                results.AppendLine(string.Format("   Total Items:      {0:N0}", embeddings.Count));
                results.AppendLine(string.Format("   Total Time:       {0:N0} ms", embeddingStopwatch.ElapsedMilliseconds));
                results.AppendLine(string.Format("   Avg per Item:     {0:F2} ms", avgTimePerItem));
                results.AppendLine(string.Format("   Vector Dimension: {0:N0}", vocabulary.TotalFeatures));
                results.AppendLine();

                UpdatePeakMemory(ref peakMemoryBytes);
                progress?.Report(60);

                // Step 4: Benchmark User Profile Generation (80% progress)
                cancellationToken.ThrowIfCancellationRequested();
                results.AppendLine("4. User Profile Generation");
                results.AppendLine("   " + new string('-', 76));

                var users = _userManager.Users.ToList();
                var metadata = library.ToDictionary(m => m.Id);

                // Use the embeddings directly if it's already a dictionary, otherwise convert
                var embeddingsDict = embeddings as IReadOnlyDictionary<Guid, ItemEmbedding>
                    ?? embeddings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (users.Count > 0)
                {
                    var profileTimes = new List<long>();
                    var watchCounts = new List<int>();

                    // Benchmark up to 10 users
                    foreach (var user in users.Take(10))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var profileStopwatch = Stopwatch.StartNew();
                        var profile = _userProfileService.BuildUserProfile(user.Id, embeddingsDict, config);
                        profileStopwatch.Stop();

                        if (profile != null)
                        {
                            profileTimes.Add(profileStopwatch.ElapsedMilliseconds);
                            watchCounts.Add(profile.WatchedItemCount);
                        }
                        else
                        {
                            _logger.LogDebug("User {UserId} has no watch history, skipping", user.Id);
                        }
                    }

                    if (profileTimes.Count > 0)
                    {
                        var avgProfileTime = profileTimes.Average();
                        var avgWatchCount = watchCounts.Count > 0 ? watchCounts.Average() : 0;

                        results.AppendLine(string.Format("   Users Profiled:   {0:N0}", profileTimes.Count));
                        results.AppendLine(string.Format("   Avg Watch Count:  {0:F1}", avgWatchCount));
                        results.AppendLine(string.Format("   Avg Profile Time: {0:F2} ms", avgProfileTime));
                    }
                    else
                    {
                        results.AppendLine("   No users with watch history found");
                    }
                }
                else
                {
                    results.AppendLine("   No users found");
                }

                results.AppendLine();
                UpdatePeakMemory(ref peakMemoryBytes);
                progress?.Report(80);

                // Step 5: Benchmark Recommendation Scoring (95% progress)
                cancellationToken.ThrowIfCancellationRequested();
                results.AppendLine("5. Recommendation Scoring");
                results.AppendLine("   " + new string('-', 76));

                // Find a user with watch history to benchmark
                UserProfile? testProfile = null;
                Guid testUserId = Guid.Empty;

                foreach (var user in users)
                {
                    testProfile = _userProfileService.BuildUserProfile(user.Id, embeddingsDict, config);
                    if (testProfile != null)
                    {
                        testUserId = user.Id;
                        break;
                    }
                }

                if (testProfile != null)
                {
                    var movieScoringStopwatch = Stopwatch.StartNew();
                    var movieRecs = _recommendationEngine.GenerateRecommendations(
                        testUserId,
                        testProfile,
                        embeddingsDict,
                        metadata,
                        config,
                        MediaType.Movie,
                        config.MovieRecommendationCount);
                    movieScoringStopwatch.Stop();

                    var tvScoringStopwatch = Stopwatch.StartNew();
                    var tvRecs = _recommendationEngine.GenerateRecommendations(
                        testUserId,
                        testProfile,
                        embeddingsDict,
                        metadata,
                        config,
                        MediaType.Series,
                        config.TvRecommendationCount);
                    tvScoringStopwatch.Stop();

                    var movieCandidates = metadata.Values.Count(m => m.Type == MediaType.Movie);
                    var tvCandidates = metadata.Values.Count(m => m.Type == MediaType.Series);

                    results.AppendLine(string.Format("   Movie Candidates: {0:N0}", movieCandidates));
                    results.AppendLine(string.Format("   Movie Recs:       {0:N0}", movieRecs.Count));
                    results.AppendLine(string.Format("   Movie Time:       {0:N0} ms", movieScoringStopwatch.ElapsedMilliseconds));
                    results.AppendLine();
                    results.AppendLine(string.Format("   TV Candidates:    {0:N0}", tvCandidates));
                    results.AppendLine(string.Format("   TV Recs:          {0:N0}", tvRecs.Count));
                    results.AppendLine(string.Format("   TV Time:          {0:N0} ms", tvScoringStopwatch.ElapsedMilliseconds));
                }
                else
                {
                    results.AppendLine("   No users with watch history found");
                }

                results.AppendLine();
                UpdatePeakMemory(ref peakMemoryBytes);
                progress?.Report(95);

                // Final Summary
                overallStopwatch.Stop();
                results.AppendLine("================================================================================");
                results.AppendLine("Summary");
                results.AppendLine("================================================================================");
                results.AppendLine(string.Format("   Total Time:       {0:N0} ms ({1:F2} seconds)", overallStopwatch.ElapsedMilliseconds, overallStopwatch.Elapsed.TotalSeconds));
                results.AppendLine(string.Format("   Peak Memory:      {0:F2} MB", peakMemoryBytes / (1024.0 * 1024.0)));
                results.AppendLine(string.Format("   Memory Delta:     {0:F2} MB", (peakMemoryBytes - initialMemory) / (1024.0 * 1024.0)));
                results.AppendLine();

                // Performance Assessment
                results.AppendLine("Performance Assessment:");
                results.AppendLine("   " + new string('-', 76));

                var totalSeconds = overallStopwatch.Elapsed.TotalSeconds;
                const int TargetSeconds = 120; // 2 minutes for 2k items, 2 users

                if (totalSeconds < TargetSeconds)
                {
                    results.AppendLine(string.Format("   [PASS] Time: Completed in {0:F1}s (target: <{1}s)", totalSeconds, TargetSeconds));
                }
                else if (totalSeconds < TargetSeconds * 1.5)
                {
                    results.AppendLine(string.Format("   [WARN] Time: Completed in {0:F1}s (target: <{1}s)", totalSeconds, TargetSeconds));
                }
                else
                {
                    results.AppendLine(string.Format("   [FAIL] Time: Completed in {0:F1}s (target: <{1}s)", totalSeconds, TargetSeconds));
                }

                var memoryMB = peakMemoryBytes / (1024.0 * 1024.0);
                const int TargetMemoryMB = 100;

                if (memoryMB < TargetMemoryMB)
                {
                    results.AppendLine(string.Format("   [PASS] Memory: {0:F1} MB (target: <{1} MB)", memoryMB, TargetMemoryMB));
                }
                else
                {
                    results.AppendLine(string.Format("   [WARN] Memory: {0:F1} MB (target: <{1} MB)", memoryMB, TargetMemoryMB));
                }

                results.AppendLine();
                results.AppendLine("================================================================================");

                // Log and save results
                LogAndSaveResults(results.ToString());

                progress?.Report(100);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Benchmark task was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Benchmark task failed");
                results.AppendLine();
                results.AppendLine(string.Format("ERROR: Benchmark failed - {0}", ex.Message));
                results.AppendLine(ex.ToString());
                LogAndSaveResults(results.ToString());
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Manual execution only - no automatic triggers
            return Array.Empty<TaskTriggerInfo>();
        }

        private static void UpdatePeakMemory(ref long peakMemoryBytes)
        {
            // Get current memory without forcing GC (more accurate for real-world usage)
            var currentMemory = GC.GetTotalMemory(forceFullCollection: false);
            if (currentMemory > peakMemoryBytes)
            {
                peakMemoryBytes = currentMemory;
            }
        }

        private void LogAndSaveResults(string results)
        {
            // Log to Jellyfin log
            _logger.LogInformation("Benchmark results:\n{Results}", results);

            // Save to file in plugin data directory
            try
            {
                var pluginDataPath = Plugin.Instance?.DataFolderPath;
                if (!string.IsNullOrEmpty(pluginDataPath))
                {
                    Directory.CreateDirectory(pluginDataPath);
                    var resultsPath = Path.Combine(pluginDataPath, "benchmark_results.txt");
                    File.WriteAllText(resultsPath, results);
                    _logger.LogInformation("Benchmark results saved to {Path}", resultsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save benchmark results to file");
            }
        }
    }
}
