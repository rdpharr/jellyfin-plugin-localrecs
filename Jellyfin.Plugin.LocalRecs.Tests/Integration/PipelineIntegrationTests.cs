using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Tests.Fixtures;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Integration
{
    /// <summary>
    /// Full pipeline integration tests that exercise the entire recommendation flow.
    /// These tests validate that all components work together correctly, using mocked
    /// Jellyfin services but real plugin service implementations.
    /// </summary>
    [Trait("Category", "Integration")]
    public class PipelineIntegrationTests
    {
        private readonly Mock<IUserDataManager> _mockUserDataManager;
        private readonly Mock<IUserManager> _mockUserManager;
        private readonly Mock<ILibraryManager> _mockLibraryManager;
        private readonly PluginConfiguration _config;
        private readonly Guid _testUserId;
        private readonly User _testUser;

        public PipelineIntegrationTests()
        {
            _mockUserDataManager = new Mock<IUserDataManager>();
            _mockUserManager = new Mock<IUserManager>();
            _mockLibraryManager = new Mock<ILibraryManager>();

            _testUserId = Guid.NewGuid();
            _testUser = new User("TestUser", "Default", "Default");
            _mockUserManager.Setup(m => m.GetUserById(_testUserId)).Returns(_testUser);

            _config = new PluginConfiguration
            {
                FavoriteBoost = 2.0,
                RewatchBoost = 1.5,
                RecencyDecayHalfLifeDays = 365.0,
                MinWatchedItemsForPersonalization = 3,
                MovieRecommendationCount = 25,
                TvRecommendationCount = 25
            };
        }

        #region Full Pipeline Tests

        [Fact]
        public void FullPipeline_SciFiFan_RecommendsSimilarContent()
        {
            // Arrange - Create library with diverse content
            var library = TestMediaLibrary.CreateTestLibrary();
            var sciFiWatchHistory = TestUserData.CreateSciFiFanHistory(library);

            // Build vocabulary and embeddings (real services)
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup mocks for user profile building
            SetupUserDataMocks(sciFiWatchHistory, library);

            // Build user profile (real service, mocked Jellyfin)
            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            // Generate recommendations (real service, mocked Jellyfin)
            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert
            recommendations.Should().NotBeEmpty("should produce recommendations");
            recommendations.Should().AllSatisfy(r =>
            {
                r.Score.Should().BeInRange(0, 1, "scores should be normalized");
                metadata.Should().ContainKey(r.ItemId, "recommended items should exist in library");
            });

            // Verify sci-fi content is ranked highly
            var topRecommendations = recommendations.Take(3).ToList();
            var topItems = topRecommendations.Select(r => metadata[r.ItemId]).ToList();

            // At least one of the top 3 should be sci-fi related
            topItems.Should().Contain(item =>
                item.Genres.Contains("Science Fiction") ||
                item.Genres.Contains("Action"),
                "top recommendations for sci-fi fan should include sci-fi or action content");
        }

        [Fact]
        public void FullPipeline_DramaFan_RecommendsDramaContent()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestLibrary();
            var dramaWatchHistory = TestUserData.CreateDramaFanHistory(library);

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(dramaWatchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert
            recommendations.Should().NotBeEmpty();

            var topItems = recommendations.Take(3)
                .Select(r => metadata[r.ItemId])
                .ToList();

            // Drama fan should get drama-related recommendations
            topItems.Should().Contain(item =>
                item.Genres.Contains("Drama") ||
                item.Genres.Contains("Crime"),
                "top recommendations for drama fan should include drama content");
        }

        [Fact]
        public void FullPipeline_ColdStartUser_ReturnsHighlyRatedContent()
        {
            // Arrange - User with fewer than MinWatchedItems
            var library = TestMediaLibrary.CreateTestLibrary();

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup user with only 1 watched item (below threshold of 3)
            var singleWatch = new List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)>
            {
                (library[0], false, 1, 7)
            };
            SetupUserDataMocks(singleWatch, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert
            recommendations.Should().NotBeEmpty("cold start should return recommendations");

            // Cold start should return highly-rated items
            var topItems = recommendations.Take(5)
                .Select(r => metadata[r.ItemId])
                .ToList();

            var averageRating = topItems
                .Where(i => i.CommunityRating.HasValue)
                .Average(i => i.CommunityRating!.Value);

            averageRating.Should().BeGreaterThan(7.5f, "cold start should return highly-rated content");
        }

        [Fact]
        public void FullPipeline_ExcludesWatchedItems()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestLibrary();
            var watchedItems = library.Take(5).ToList();
            var watchHistory = watchedItems.Select(item =>
                (Item: item, IsFavorite: false, PlayCount: 1, DaysAgo: 7)).ToList();

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert
            var watchedItemIds = watchedItems.Select(i => i.Id).ToHashSet();
            recommendations.Should().NotContain(r => watchedItemIds.Contains(r.ItemId),
                "recommendations should not include already-watched items");
        }

        #endregion

        #region Performance Tests

        [Theory]
        [InlineData(15)]   // Default library size
        [InlineData(50)]   // Small library
        [InlineData(100)]  // Medium library
        public void FullPipeline_CompletesWithinTimeLimit(int itemCount)
        {
            // Arrange
            var library = GenerateScalableLibrary(itemCount);
            var watchHistory = library.Take(Math.Max(3, itemCount / 10))
                .Select(item => (Item: item, IsFavorite: false, PlayCount: 1, DaysAgo: 7))
                .ToList();

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            var stopwatch = Stopwatch.StartNew();

            // Act
            var vocabulary = vocabBuilder.BuildVocabulary(library);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);
            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            stopwatch.Stop();

            // Assert
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
                $"full pipeline for {itemCount} items should complete within 5 seconds");
            recommendations.Should().NotBeEmpty();
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void FullPipeline_EmptyLibrary_ThrowsArgumentException()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // No watch history
            SetupUserDataMocks(new List<(MediaItemMetadata, bool, int, int)>(), library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            // Act & Assert - Empty library should throw early
            var action = () => profileService.BuildUserProfile(_testUserId, embeddings, _config);
            action.Should().Throw<ArgumentException>("empty library is an invalid state");
        }

        [Fact]
        public void FullPipeline_NoWatchHistory_ReturnsColdStartRecommendations()
        {
            // Arrange - Create library with items that have community ratings
            var library = TestMediaLibrary.CreateTestLibrary();

            // Ensure items have community ratings for cold-start sorting
            foreach (var item in library)
            {
                if (item.CommunityRating == null)
                {
                    item.CommunityRating = 5.0f + (float)(new Random(item.Id.GetHashCode()).NextDouble() * 4.0);
                }
            }

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // No watch history - empty list
            SetupUserDataMocks(new List<(MediaItemMetadata, bool, int, int)>(), library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act - Build profile (should return null for no watch history)
            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert - Profile should be null
            userProfile.Should().BeNull("user has no watch history");

            // Act - Generate recommendations with null profile (cold-start path)
            var movieRecommendations = engine.GenerateRecommendations(
                _testUserId,
                userProfile,
                embeddings,
                metadata,
                _config,
                MediaType.Movie,
                _config.MovieRecommendationCount);

            var tvRecommendations = engine.GenerateRecommendations(
                _testUserId,
                userProfile,
                embeddings,
                metadata,
                _config,
                MediaType.Series,
                _config.TvRecommendationCount);

            // Assert - Cold-start should return top-rated content
            movieRecommendations.Should().NotBeEmpty("cold-start should recommend highly-rated movies");
            tvRecommendations.Should().NotBeEmpty("cold-start should recommend highly-rated TV shows");

            // Verify recommendations are sorted by community rating (descending)
            var movieRatings = movieRecommendations
                .Select(r => metadata[r.ItemId].CommunityRating ?? 0)
                .ToList();
            movieRatings.Should().BeInDescendingOrder("cold-start recommendations should be sorted by rating");

            var tvRatings = tvRecommendations
                .Select(r => metadata[r.ItemId].CommunityRating ?? 0)
                .ToList();
            tvRatings.Should().BeInDescendingOrder("cold-start recommendations should be sorted by rating");

            // Verify the score in ScoredRecommendation matches the normalized community rating (0-1 range)
            foreach (var rec in movieRecommendations)
            {
                var expectedRating = (metadata[rec.ItemId].CommunityRating ?? 0) / 10.0f;
                Math.Abs(rec.Score - expectedRating).Should().BeLessThan(0.001f,
                    "cold-start score should be the normalized community rating (0-1 range)");
            }
        }

        [Fact]
        public void FullPipeline_AllItemsWatched_ReturnsEmptyRecommendations()
        {
            // Arrange
            var library = TestMediaLibrary.CreateMinimalLibrary();
            var allWatched = library.Select(item =>
                (Item: item, IsFavorite: false, PlayCount: 1, DaysAgo: 7)).ToList();

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(allWatched, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert
            recommendations.Should().BeEmpty("all items watched should leave no candidates");
        }

        [Fact]
        public void FullPipeline_ItemsWithoutGenres_StillProducesRecommendations()
        {
            // Arrange - Library with minimal metadata
            var library = new List<MediaItemMetadata>
            {
                new MediaItemMetadata(Guid.NewGuid(), "Movie 1", MediaType.Movie)
                {
                    ReleaseYear = 2020,
                    CommunityRating = 8.0f
                },
                new MediaItemMetadata(Guid.NewGuid(), "Movie 2", MediaType.Movie)
                {
                    ReleaseYear = 2021,
                    CommunityRating = 7.5f
                },
                new MediaItemMetadata(Guid.NewGuid(), "Movie 3", MediaType.Movie)
                {
                    ReleaseYear = 2019,
                    CommunityRating = 9.0f
                }
            };

            var watchHistory = new List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)>
            {
                (library[0], false, 1, 7)
            };

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, _config);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, _config);

            // Assert - Should still work with year and rating dimensions
            recommendations.Should().NotBeEmpty("should use year/rating when genres unavailable");
        }

        #endregion

        #region Configuration Edge Cases

        [Fact]
        public void FullPipeline_ZeroRecencyHalfLife_ThrowsArgumentException()
        {
            // Arrange - Edge case: zero half-life (would cause division by zero)
            var library = TestMediaLibrary.CreateTestLibrary();
            var watchHistory = TestUserData.CreateSciFiFanHistory(library);

            var configWithZeroHalfLife = new PluginConfiguration
            {
                FavoriteBoost = 2.0,
                RewatchBoost = 1.5,
                RecencyDecayHalfLifeDays = 0.0, // Invalid value!
                MinWatchedItemsForPersonalization = 3
            };

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            // Act & Assert - Should throw because zero half-life is invalid
            var action = () => profileService.BuildUserProfile(_testUserId, embeddings, configWithZeroHalfLife);
            action.Should().Throw<ArgumentException>("zero half-life would cause division by zero");
        }

        [Fact]
        public void FullPipeline_VerySmallRecencyHalfLife_HandlesGracefully()
        {
            // Arrange - Very small but valid half-life
            var library = TestMediaLibrary.CreateTestLibrary();
            var watchHistory = TestUserData.CreateSciFiFanHistory(library);

            var configWithSmallHalfLife = new PluginConfiguration
            {
                FavoriteBoost = 2.0,
                RewatchBoost = 1.5,
                RecencyDecayHalfLifeDays = 0.001, // Very small but valid
                MinWatchedItemsForPersonalization = 3
            };

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act & Assert - Should handle gracefully
            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, configWithSmallHalfLife);
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, configWithSmallHalfLife);

            recommendations.Should().AllSatisfy(r =>
            {
                double.IsNaN(r.Score).Should().BeFalse("scores should not be NaN");
                double.IsInfinity(r.Score).Should().BeFalse("scores should not be infinite");
            });
        }

        [Fact]
        public void FullPipeline_ExtremeBoostValues_HandlesGracefully()
        {
            // Arrange - Very high boost values
            var library = TestMediaLibrary.CreateTestLibrary();
            var watchHistory = TestUserData.CreateSciFiFanHistory(library);

            var configWithExtremeBoosts = new PluginConfiguration
            {
                FavoriteBoost = 100.0,  // Extreme
                RewatchBoost = 50.0,    // Extreme
                RecencyDecayHalfLifeDays = 1.0, // Very short
                MinWatchedItemsForPersonalization = 3
            };

            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(watchHistory, library);

            var profileService = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            var engine = new RecommendationEngine(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<RecommendationEngine>.Instance);

            // Act & Assert
            var userProfile = profileService.BuildUserProfile(_testUserId, embeddings, configWithExtremeBoosts);
            var recommendations = engine.GenerateRecommendations(
                _testUserId, userProfile, embeddings, metadata, configWithExtremeBoosts);

            recommendations.Should().AllSatisfy(r =>
            {
                r.Score.Should().BeInRange(0, 1, "scores should remain normalized");
                double.IsNaN(r.Score).Should().BeFalse("scores should not be NaN");
                double.IsInfinity(r.Score).Should().BeFalse("scores should not be infinite");
            });
        }

        #endregion

        #region Helper Methods

        private void SetupUserDataMocks(
            List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)> watchHistory,
            List<MediaItemMetadata> library)
        {
            var watchedItemIds = watchHistory.Select(w => w.Item.Id).ToHashSet();

            foreach (var item in library)
            {
                // Create a minimal mock BaseItem - we can't mock Id since it's not virtual
                // So we return any BaseItem and match on it being called with that item ID
                var mockItem = new Mock<BaseItem>();
                _mockLibraryManager.Setup(m => m.GetItemById(item.Id)).Returns(mockItem.Object);

                var watch = watchHistory.FirstOrDefault(w => w.Item.Id == item.Id);
                if (watchedItemIds.Contains(item.Id))
                {
                    var userData = new UserItemData
                    {
                        Key = item.Id.ToString(),
                        Played = true,
                        IsFavorite = watch.IsFavorite,
                        PlayCount = watch.PlayCount,
                        LastPlayedDate = DateTime.UtcNow.AddDays(-watch.DaysAgo)
                    };

                    // Match on the specific mock item returned by GetItemById
                    _mockUserDataManager.Setup(m => m.GetUserData(_testUser, mockItem.Object))
                        .Returns(userData);
                }
                else
                {
                    var userData = new UserItemData
                    {
                        Key = item.Id.ToString(),
                        Played = false,
                        IsFavorite = false,
                        PlayCount = 0
                    };

                    _mockUserDataManager.Setup(m => m.GetUserData(_testUser, mockItem.Object))
                        .Returns(userData);
                }
            }
        }

        private List<MediaItemMetadata> GenerateScalableLibrary(int count)
        {
            var genres = new[] { "Action", "Comedy", "Drama", "Horror", "Science Fiction", "Thriller", "Romance", "Animation" };
            var actors = Enumerable.Range(1, 50).Select(i => $"Actor {i}").ToArray();
            var directors = Enumerable.Range(1, 20).Select(i => $"Director {i}").ToArray();
            var random = new Random(42); // Fixed seed for reproducibility

            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < count; i++)
            {
                var item = new MediaItemMetadata(
                    Guid.NewGuid(),
                    $"Movie {i + 1}",
                    i % 5 == 0 ? MediaType.Series : MediaType.Movie)
                {
                    ReleaseYear = 1990 + random.Next(35),
                    CommunityRating = 5.0f + (float)(random.NextDouble() * 5.0)
                };

                // Add 1-3 genres
                var numGenres = random.Next(1, 4);
                for (int g = 0; g < numGenres; g++)
                {
                    item.AddGenre(genres[random.Next(genres.Length)]);
                }

                // Add 1-3 actors
                var numActors = random.Next(1, 4);
                for (int a = 0; a < numActors; a++)
                {
                    item.AddActor(actors[random.Next(actors.Length)]);
                }

                // Add 1 director
                item.AddDirector(directors[random.Next(directors.Length)]);

                library.Add(item);
            }

            return library;
        }

        #endregion
    }
}
