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
    /// Service for building user taste profiles from watch history.
    /// Aggregates weighted embeddings into normalized taste vectors.
    /// </summary>
    public class UserProfileService
    {
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<UserProfileService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfileService"/> class.
        /// </summary>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        public UserProfileService(
            IUserDataManager userDataManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<UserProfileService> logger)
        {
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Builds a user profile from watch history and embeddings.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="embeddings">Dictionary of item embeddings.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>UserProfile with taste vector, or null if user has no watch history.</returns>
        /// <exception cref="ArgumentNullException">Thrown when embeddings or config is null.</exception>
        /// <exception cref="ArgumentException">Thrown when embeddings dictionary is empty.</exception>
        public UserProfile? BuildUserProfile(
            Guid userId,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            PluginConfiguration config)
        {
            if (embeddings == null)
            {
                throw new ArgumentNullException(nameof(embeddings));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (embeddings.Count == 0)
            {
                throw new ArgumentException("Embeddings dictionary cannot be empty", nameof(embeddings));
            }

            _logger.LogInformation("Building user profile for user {UserId}", userId);

            // Get watch records for this user
            var watchRecords = GetWatchRecords(userId, embeddings.Keys);

            if (watchRecords.Count == 0)
            {
                _logger.LogWarning("No watch history found for user {UserId}", userId);
                return null; // Return null to trigger cold-start recommendations
            }

            _logger.LogInformation("Found {Count} watched items for user {UserId}", watchRecords.Count, userId);

            // Compute weighted taste vector
            var tasteVector = ComputeTasteVector(watchRecords, embeddings, config);

            // Compute rating statistics from watched items
            var (avgCommunity, avgCritic, communityStdDev, criticStdDev) = ComputeRatingStatistics(watchRecords);

            var profile = new UserProfile(userId, tasteVector)
            {
                WatchedItemCount = watchRecords.Count,
                AverageCommunityRating = avgCommunity,
                AverageCriticRating = avgCritic,
                CommunityRatingStdDev = communityStdDev,
                CriticRatingStdDev = criticStdDev
            };

            _logger.LogInformation(
                "Built profile for user {UserId}: {Count} items, AvgCommunity={AvgCommunity:F2}, AvgCritic={AvgCritic:F2}",
                userId,
                watchRecords.Count,
                avgCommunity ?? 0,
                avgCritic ?? 0);

            return profile;
        }

        /// <summary>
        /// Gets watch records for a user.
        /// Only includes items that have been fully watched (Played = true).
        /// This ensures the taste profile reflects content the user actually consumed.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="availableItemIds">Available item IDs (from embeddings).</param>
        /// <returns>List of watch records.</returns>
        private List<WatchRecord> GetWatchRecords(Guid userId, IEnumerable<Guid> availableItemIds)
        {
            var records = new List<WatchRecord>();
            var user = _userManager.GetUserById(userId);

            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", userId);
                return records;
            }

            var itemIdSet = availableItemIds.ToHashSet();

            foreach (var itemId in itemIdSet)
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    continue;
                }

                var userData = _userDataManager.GetUserData(user, item);

                if (userData == null)
                {
                    continue;
                }

                // Only include items that are fully watched (Played = true).
                // For movies, this means the movie was completed.
                // For series, this means all episodes were watched.
                // This ensures the taste profile reflects content the user actually consumed,
                // not partially watched or abandoned content.
                if (!userData.Played)
                {
                    continue;
                }

                var record = new WatchRecord(itemId, userId, userData.LastPlayedDate ?? DateTime.UtcNow)
                {
                    IsFavorite = userData.IsFavorite,
                    PlayCount = userData.PlayCount > 0 ? userData.PlayCount : 1,
                    CommunityRating = item.CommunityRating,
                    CriticRating = item.CriticRating
                };

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Computes the user's taste vector as a weighted sum of watched item embeddings.
        /// </summary>
        /// <param name="watchRecords">User's watch records.</param>
        /// <param name="embeddings">Item embeddings.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>Normalized taste vector.</returns>
        private float[] ComputeTasteVector(
            List<WatchRecord> watchRecords,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            PluginConfiguration config)
        {
            // Get dimension from first embedding
            var firstEmbedding = embeddings.Values.FirstOrDefault();
            if (firstEmbedding == null)
            {
                throw new InvalidOperationException("No embeddings available");
            }

            var dimension = firstEmbedding.Dimensions;
            var weightedSum = new float[dimension];
            float totalWeight = 0;

            var now = DateTime.UtcNow;

            // Accumulate weighted embeddings for each watched item
            foreach (var record in watchRecords)
            {
                if (!embeddings.TryGetValue(record.ItemId, out var embedding))
                {
                    continue; // Skip items without embeddings
                }

                // Compute combined weight: recency decay × favorite boost × rewatch boost
                var daysSince = (now - record.LastPlayedDate).TotalDays;
                var weight = WeightCalculator.ComputeCombinedWeight(
                    daysSince,
                    config.RecencyDecayHalfLifeDays,
                    record.IsFavorite,
                    (float)config.FavoriteBoost,
                    Math.Max(1, record.PlayCount),
                    (float)config.RewatchBoost);

                // Accumulate weighted vectors
                for (int i = 0; i < dimension; i++)
                {
                    weightedSum[i] += embedding.Vector[i] * weight;
                }

                totalWeight += weight;
            }

            // Normalize by total weight to get weighted average, then normalize to unit length
            if (totalWeight > 0)
            {
                for (int i = 0; i < dimension; i++)
                {
                    weightedSum[i] /= totalWeight;
                }
            }

            return VectorMath.Normalize(weightedSum);
        }

        /// <summary>
        /// Computes rating statistics from watched items.
        /// Uses ratings cached in WatchRecord to avoid duplicate library lookups.
        /// </summary>
        /// <param name="watchRecords">User's watch records (with ratings already populated).</param>
        /// <returns>Rating statistics with averages and standard deviations.</returns>
        private (float? AvgCommunityRating, float? AvgCriticRating, float CommunityStdDev, float CriticStdDev) ComputeRatingStatistics(
            List<WatchRecord> watchRecords)
        {
            // Extract ratings from watch records (already populated during GetWatchRecords)
            var communityRatings = watchRecords
                .Where(r => r.CommunityRating.HasValue)
                .Select(r => r.CommunityRating!.Value)
                .ToList();

            var criticRatings = watchRecords
                .Where(r => r.CriticRating.HasValue)
                .Select(r => r.CriticRating!.Value)
                .ToList();

            // Compute community rating statistics
            float? avgCommunity = null;
            float communityStdDev = 0f;
            if (communityRatings.Any())
            {
                avgCommunity = communityRatings.Average();
                if (communityRatings.Count > 1)
                {
                    var variance = communityRatings.Average(r => Math.Pow(r - avgCommunity.Value, 2));
                    communityStdDev = (float)Math.Sqrt(variance);
                }
            }

            // Compute critic rating statistics
            float? avgCritic = null;
            float criticStdDev = 0f;
            if (criticRatings.Any())
            {
                avgCritic = criticRatings.Average();
                if (criticRatings.Count > 1)
                {
                    var variance = criticRatings.Average(r => Math.Pow(r - avgCritic.Value, 2));
                    criticStdDev = (float)Math.Sqrt(variance);
                }
            }

            return (avgCommunity, avgCritic, communityStdDev, criticStdDev);
        }
    }
}
