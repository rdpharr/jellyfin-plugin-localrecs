using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for refreshing recommendations for users.
    /// Used by the scheduled task to generate recommendations for all users.
    /// No caching - computes fresh recommendations on every run.
    /// </summary>
    public class RecommendationRefreshService
    {
        private readonly ILogger<RecommendationRefreshService> _logger;
        private readonly LibraryAnalysisService _libraryAnalysisService;
        private readonly VocabularyBuilder _vocabularyBuilder;
        private readonly EmbeddingService _embeddingService;
        private readonly UserProfileService _userProfileService;
        private readonly RecommendationEngine _recommendationEngine;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationRefreshService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryAnalysisService">Library analysis service.</param>
        /// <param name="vocabularyBuilder">Vocabulary builder.</param>
        /// <param name="embeddingService">Embedding service.</param>
        /// <param name="userProfileService">User profile service.</param>
        /// <param name="recommendationEngine">Recommendation engine.</param>
        public RecommendationRefreshService(
            ILogger<RecommendationRefreshService> logger,
            LibraryAnalysisService libraryAnalysisService,
            VocabularyBuilder vocabularyBuilder,
            EmbeddingService embeddingService,
            UserProfileService userProfileService,
            RecommendationEngine recommendationEngine)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryAnalysisService = libraryAnalysisService ?? throw new ArgumentNullException(nameof(libraryAnalysisService));
            _vocabularyBuilder = vocabularyBuilder ?? throw new ArgumentNullException(nameof(vocabularyBuilder));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _userProfileService = userProfileService ?? throw new ArgumentNullException(nameof(userProfileService));
            _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        }

        /// <summary>
        /// Computes fresh embeddings for the library.
        /// Always recomputes to ensure recommendations reflect current watch history.
        /// </summary>
        /// <returns>Tuple of embeddings dictionary and metadata dictionary.</returns>
        public (IReadOnlyDictionary<Guid, ItemEmbedding> Embeddings, IReadOnlyDictionary<Guid, MediaItemMetadata> Metadata) ComputeEmbeddings()
        {
            // Get library metadata (always fresh)
            var library = _libraryAnalysisService.GetAllMediaItems();
            var metadata = library.ToDictionary(m => m.Id);

            // Always compute fresh embeddings to reflect current watch history
            _logger.LogInformation("Computing fresh embeddings for {Count} items", library.Count);

            var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

            var vocabulary = _vocabularyBuilder.BuildVocabulary(
                library,
                config.MaxVocabularyActors,
                config.MaxVocabularyDirectors,
                config.MaxVocabularyTags);
            var embeddings = _embeddingService.ComputeEmbeddings(library, vocabulary);

            var embeddingsDict = embeddings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return (embeddingsDict, metadata);
        }

        /// <summary>
        /// Generates recommendations for a single user.
        /// Returns tuple of (movie recommendations, TV recommendations).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="embeddings">Pre-computed embeddings.</param>
        /// <param name="metadata">Item metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Tuple of movie and TV recommendations.</returns>
        public (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv) GenerateRecommendationsForUser(
            Guid userId,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config)
        {
            _logger.LogDebug("Generating recommendations for user {UserId}", userId);

            try
            {
                // Build user profile (returns null if no watch history)
                var profile = _userProfileService.BuildUserProfile(userId, embeddings, config);

                // Generate movie recommendations (will use cold-start if profile is null)
                var movieRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Movie,
                    config.MovieRecommendationCount);

                // Generate TV recommendations (will use cold-start if profile is null)
                var tvRecs = _recommendationEngine.GenerateRecommendations(
                    userId,
                    profile,
                    embeddings,
                    metadata,
                    config,
                    MediaType.Series,
                    config.TvRecommendationCount);

                _logger.LogInformation(
                    "Generated recommendations for user {UserId}: {MovieCount} movies, {TvCount} TV",
                    userId,
                    movieRecs.Count,
                    tvRecs.Count);

                return (movieRecs, tvRecs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate recommendations for user {UserId}", userId);
                return (new List<ScoredRecommendation>(), new List<ScoredRecommendation>());
            }
        }

        /// <summary>
        /// Generates recommendations for multiple users efficiently.
        /// Computes embeddings once and reuses them for all users.
        /// </summary>
        /// <param name="userIds">List of user IDs to process.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Dictionary mapping user IDs to their recommendations (movies, TV).</returns>
        public Task<Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>> GenerateRecommendationsForMultipleUsersAsync(
            IReadOnlyList<Guid> userIds,
            PluginConfiguration config)
        {
            var results = new Dictionary<Guid, (List<ScoredRecommendation> Movies, List<ScoredRecommendation> Tv)>();

            if (userIds.Count == 0)
            {
                return Task.FromResult(results);
            }

            _logger.LogInformation("Generating recommendations for {Count} users", userIds.Count);

            // Compute embeddings once for all users
            var (embeddings, metadata) = ComputeEmbeddings();

            foreach (var userId in userIds)
            {
                var recs = GenerateRecommendationsForUser(userId, embeddings, metadata, config);
                results[userId] = recs;
            }

            _logger.LogInformation("Successfully generated recommendations for {Count}/{Total} users", results.Count, userIds.Count);

            return Task.FromResult(results);
        }
    }
}
