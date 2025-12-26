using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Utilities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for generating personalized recommendations.
    /// Scores candidates using cosine similarity between user taste vectors and item embeddings.
    /// </summary>
    public class RecommendationEngine
    {
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<RecommendationEngine> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecommendationEngine"/> class.
        /// </summary>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        public RecommendationEngine(
            IUserDataManager userDataManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<RecommendationEngine> logger)
        {
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Generates recommendations for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="userProfile">The user's taste profile.</param>
        /// <param name="embeddings">Dictionary of item embeddings.</param>
        /// <param name="metadata">Dictionary of item metadata.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="mediaType">Filter to specific media type (null = all types).</param>
        /// <param name="maxResults">Maximum number of recommendations to return.</param>
        /// <returns>List of scored recommendations, ordered by score descending.</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
        /// <exception cref="ArgumentException">Thrown when embeddings or metadata are empty.</exception>
        public List<ScoredRecommendation> GenerateRecommendations(
            Guid userId,
            UserProfile? userProfile,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            PluginConfiguration config,
            MediaType? mediaType = null,
            int maxResults = 25)
        {
            if (embeddings == null)
            {
                throw new ArgumentNullException(nameof(embeddings));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (embeddings.Count == 0)
            {
                throw new ArgumentException("Embeddings dictionary cannot be empty", nameof(embeddings));
            }

            if (metadata.Count == 0)
            {
                throw new ArgumentException("Metadata dictionary cannot be empty", nameof(metadata));
            }

            _logger.LogInformation(
                "Generating recommendations for user {UserId}, mediaType: {MediaType}, max: {MaxResults}",
                userId,
                mediaType?.ToString() ?? "All",
                maxResults);

            // Check for cold-start scenario
            if (userProfile == null || userProfile.WatchedItemCount < config.MinWatchedItemsForPersonalization)
            {
                _logger.LogInformation(
                    "Cold-start scenario for user {UserId}: {WatchedCount} watched items (min: {MinRequired})",
                    userId,
                    userProfile?.WatchedItemCount ?? 0,
                    config.MinWatchedItemsForPersonalization);

                return GenerateColdStartRecommendations(userId, metadata, mediaType, maxResults);
            }

            // Get unwatched candidates
            var candidates = GetUnwatchedCandidates(userId, embeddings.Keys, metadata, mediaType, config);

            if (candidates.Count == 0)
            {
                _logger.LogWarning("No unwatched candidates found for user {UserId}", userId);
                return new List<ScoredRecommendation>();
            }

            _logger.LogInformation(
                "Found {CandidateCount} unwatched candidates for user {UserId}",
                candidates.Count,
                userId);

            // Score all candidates
            var scoredCandidates = new List<ScoredRecommendation>();

            foreach (var candidateId in candidates)
            {
                if (!embeddings.TryGetValue(candidateId, out var embedding))
                {
                    continue; // Skip if no embedding
                }

                var score = ScoreCandidate(userProfile, embedding);

                scoredCandidates.Add(score);
            }

            // Sort by score descending and take top N
            var recommendations = scoredCandidates
                .OrderByDescending(r => r.Score)
                .Take(maxResults)
                .ToList();

            _logger.LogInformation(
                "Generated {RecommendationCount} recommendations for user {UserId}",
                recommendations.Count,
                userId);

            return recommendations;
        }

        /// <summary>
        /// Gets unwatched candidate items for a user.
        /// Excludes fully watched items and optionally partially watched series.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="availableItemIds">Available item IDs from embeddings.</param>
        /// <param name="metadata">Item metadata dictionary.</param>
        /// <param name="mediaType">Filter to specific media type.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>List of unwatched item IDs.</returns>
        private List<Guid> GetUnwatchedCandidates(
            Guid userId,
            IEnumerable<Guid> availableItemIds,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            MediaType? mediaType,
            PluginConfiguration config)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", userId);
                return new List<Guid>();
            }

            var candidates = new List<Guid>();

            foreach (var itemId in availableItemIds)
            {
                // Get item metadata for type filtering
                if (!metadata.TryGetValue(itemId, out var itemMetadata))
                {
                    continue;
                }

                // Filter by media type if specified
                if (mediaType.HasValue && itemMetadata.Type != mediaType.Value)
                {
                    continue;
                }

                // Exclude items with insufficient metadata (no genres AND no actors)
                // These produce unreliable similarity scores
                if (itemMetadata.Genres.Count == 0 && itemMetadata.Actors.Count == 0)
                {
                    continue;
                }

                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    continue;
                }

                var userData = _userDataManager.GetUserData(user, item);

                // Exclude fully watched items
                if (userData != null && userData.Played)
                {
                    continue;
                }

                // Exclude items with any playback progress (user is currently watching or has started)
                // These items will be removed from virtual library by PlayStatusSyncService
                // and should not be re-added to recommendations until fully unwatched
                if (userData != null && userData.PlaybackPositionTicks > 0)
                {
                    continue;
                }

                // Exclude partially watched series based on configuration
                if (userData != null &&
                    itemMetadata.Type == MediaType.Series &&
                    ShouldExcludePartiallyWatchedSeries(userData, config))
                {
                    continue;
                }

                candidates.Add(itemId);
            }

            return candidates;
        }

        /// <summary>
        /// Determines if a series should be excluded based on watch activity.
        /// Excludes series that have been abandoned (not watched within the threshold period).
        /// </summary>
        /// <param name="userData">User data for the series.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>True if the series should be excluded from recommendations.</returns>
        private bool ShouldExcludePartiallyWatchedSeries(
            MediaBrowser.Controller.Entities.UserItemData userData,
            PluginConfiguration config)
        {
            // If ExcludeAbandonedSeries is disabled, don't exclude any partially watched series
            if (!config.ExcludeAbandonedSeries)
            {
                return false;
            }

            // If the series has a LastPlayedDate, check if it's been abandoned
            if (userData.LastPlayedDate.HasValue)
            {
                var daysSinceLastPlayed = (DateTime.UtcNow - userData.LastPlayedDate.Value).TotalDays;

                // Exclude if NOT watched recently (abandoned = exceeds threshold)
                return daysSinceLastPlayed >= config.AbandonedSeriesThresholdDays;
            }

            // If there's no LastPlayedDate but the series has some watch progress,
            // this is an edge case. Don't exclude it - treat it as still active.
            return false;
        }

        /// <summary>
        /// Scores a candidate item against the user's taste profile using cosine similarity.
        /// </summary>
        /// <param name="userProfile">The user's taste profile.</param>
        /// <param name="candidateEmbedding">The candidate item's embedding.</param>
        /// <returns>Scored recommendation.</returns>
        private ScoredRecommendation ScoreCandidate(
            UserProfile userProfile,
            ItemEmbedding candidateEmbedding)
        {
            // Compute cosine similarity between user taste vector and item embedding
            var score = VectorMath.CosineSimilarity(
                userProfile.TasteVector,
                candidateEmbedding.Vector);

            return new ScoredRecommendation(candidateEmbedding.ItemId, score);
        }

        /// <summary>
        /// Generates recommendations for users with insufficient watch history (cold-start).
        /// Returns top-rated items from the library.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="metadata">Item metadata dictionary.</param>
        /// <param name="mediaType">Filter to specific media type.</param>
        /// <param name="maxResults">Maximum number of recommendations.</param>
        /// <returns>List of top-rated items.</returns>
        private List<ScoredRecommendation> GenerateColdStartRecommendations(
            Guid userId,
            IReadOnlyDictionary<Guid, MediaItemMetadata> metadata,
            MediaType? mediaType,
            int maxResults)
        {
            _logger.LogInformation(
                "Generating cold-start recommendations for user {UserId}",
                userId);

            // Filter to media type if specified
            var candidateMetadata = metadata.Values.AsEnumerable();

            if (mediaType.HasValue)
            {
                candidateMetadata = candidateMetadata.Where(m => m.Type == mediaType.Value);
            }

            // Get unwatched items
            var user = _userManager.GetUserById(userId);
            var unwatchedCandidates = new List<MediaItemMetadata>();

            if (user != null)
            {
                foreach (var item in candidateMetadata)
                {
                    var libraryItem = _libraryManager.GetItemById(item.Id);
                    if (libraryItem == null)
                    {
                        continue;
                    }

                    var userData = _userDataManager.GetUserData(user, libraryItem);
                    if (userData != null && userData.Played)
                    {
                        continue; // Skip watched items
                    }

                    unwatchedCandidates.Add(item);
                }
            }
            else
            {
                unwatchedCandidates = candidateMetadata.ToList();
            }

            // Sort by community rating (primary) and critic rating (secondary)
            // Normalize scores to [0-1] range to match personalized recommendation scores
            var topRated = unwatchedCandidates
                .OrderByDescending(m => m.CommunityRating ?? 0)
                .ThenByDescending(m => m.CriticRating ?? 0)
                .Take(maxResults)
                .Select(m => new ScoredRecommendation(m.Id, (m.CommunityRating ?? 0) / 10.0f))
                .ToList();

            _logger.LogInformation(
                "Generated {Count} cold-start recommendations for user {UserId}",
                topRated.Count,
                userId);

            return topRated;
        }
    }
}
