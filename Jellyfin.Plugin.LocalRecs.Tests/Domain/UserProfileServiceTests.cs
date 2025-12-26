using System;
using System.Collections.Generic;
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

namespace Jellyfin.Plugin.LocalRecs.Tests.Domain
{
    /// <summary>
    /// Tests for <see cref="UserProfileService"/>.
    /// Validates user profile building, weighting calculations, and edge case handling.
    /// </summary>
    public class UserProfileServiceTests
    {
        private readonly Mock<IUserDataManager> _mockUserDataManager;
        private readonly Mock<IUserManager> _mockUserManager;
        private readonly Mock<ILibraryManager> _mockLibraryManager;
        private readonly UserProfileService _service;
        private readonly PluginConfiguration _config;
        private readonly Guid _testUserId;
        private readonly User _testUser;

        public UserProfileServiceTests()
        {
            _mockUserDataManager = new Mock<IUserDataManager>();
            _mockUserManager = new Mock<IUserManager>();
            _mockLibraryManager = new Mock<ILibraryManager>();
            _service = new UserProfileService(
                _mockUserDataManager.Object,
                _mockUserManager.Object,
                _mockLibraryManager.Object,
                NullLogger<UserProfileService>.Instance);

            _testUserId = Guid.NewGuid();
            _testUser = new User("TestUser", "Default", "Default");

            _config = new PluginConfiguration
            {
                FavoriteBoost = 2.0,
                RewatchBoost = 1.5,
                RecencyDecayHalfLifeDays = 365.0,
                MinWatchedItemsForPersonalization = 3
            };

            // Setup user manager to return test user
            _mockUserManager.Setup(m => m.GetUserById(_testUserId)).Returns(_testUser);
        }

        [Fact]
        public void BuildUserProfile_WithValidHistory_CreatesProfile()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(library.Take(3).ToList());

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();
            profile!.UserId.Should().Be(_testUserId);
            profile.TasteVector.Should().NotBeEmpty();
            profile.WatchedItemCount.Should().Be(3);
            profile.TasteVector.Length.Should().Be(embeddings.Values.First().Dimensions);
        }

        [Fact]
        public void BuildUserProfile_TasteVector_IsNormalized()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            SetupUserDataMocks(library.Take(5).ToList());

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();
            var magnitude = Math.Sqrt(profile!.TasteVector.Sum(x => x * x));
            magnitude.Should().BeApproximately(1.0, 0.001, "taste vector should be normalized to unit length");
        }

        [Fact]
        public void BuildUserProfile_WithFavorites_FavoritesGetHigherWeight()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var matrix = library.First(m => m.Name == "The Matrix");
            var inception = library.First(m => m.Name == "Inception");

            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup: Matrix is favorite, Inception is not (same recency and play count)
            SetupSpecificUserData(matrix, isFavorite: true, playCount: 1, daysAgo: 7);
            SetupSpecificUserData(inception, isFavorite: false, playCount: 1, daysAgo: 7);

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();

            // Verify that the taste vector is more similar to the favorite item
            var matrixSimilarity = Utilities.VectorMath.CosineSimilarity(
                profile!.TasteVector,
                embeddings[matrix.Id].Vector);
            var inceptionSimilarity = Utilities.VectorMath.CosineSimilarity(
                profile.TasteVector,
                embeddings[inception.Id].Vector);

            // Matrix (favorite) should have higher influence on taste vector
            matrixSimilarity.Should().BeGreaterThan(inceptionSimilarity,
                "favorite item should have more influence due to favorite boost of {0}",
                _config.FavoriteBoost);
        }

        [Fact]
        public void BuildUserProfile_WithRewatches_RewatchesGetHigherWeight()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var godfather = library.First(m => m.Name == "The Godfather");
            var toyStory = library.First(m => m.Name == "Toy Story");

            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup: Godfather watched 5 times, Toy Story watched once (same recency, not favorites)
            SetupSpecificUserData(godfather, isFavorite: false, playCount: 5, daysAgo: 10);
            SetupSpecificUserData(toyStory, isFavorite: false, playCount: 1, daysAgo: 10);

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();
            profile!.WatchedItemCount.Should().Be(2);

            // Verify that the taste vector is more similar to the rewatched item
            var godfatherSimilarity = Utilities.VectorMath.CosineSimilarity(
                profile.TasteVector,
                embeddings[godfather.Id].Vector);
            var toyStorySimilarity = Utilities.VectorMath.CosineSimilarity(
                profile.TasteVector,
                embeddings[toyStory.Id].Vector);

            // Godfather (rewatched 5x) should have higher influence on taste vector
            godfatherSimilarity.Should().BeGreaterThan(toyStorySimilarity,
                "rewatched item should have more influence due to rewatch boost");
        }

        [Fact]
        public void BuildUserProfile_WithRecencyDecay_RecentWatchesWeightedHigher()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var recent = library.First(m => m.Name == "The Matrix");
            var old = library.First(m => m.Name == "Alien");

            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup: Recent watch (7 days ago) vs old watch (at half-life = 365 days)
            SetupSpecificUserData(recent, isFavorite: false, playCount: 1, daysAgo: 7);
            SetupSpecificUserData(old, isFavorite: false, playCount: 1, daysAgo: 365);

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();

            // Verify that the taste vector is more similar to the recent item
            var recentSimilarity = Utilities.VectorMath.CosineSimilarity(
                profile!.TasteVector,
                embeddings[recent.Id].Vector);
            var oldSimilarity = Utilities.VectorMath.CosineSimilarity(
                profile.TasteVector,
                embeddings[old.Id].Vector);

            // Recent watch should have higher influence due to recency decay
            // At 365 days (half-life), weight is 0.5x, so recent should dominate
            recentSimilarity.Should().BeGreaterThan(oldSimilarity,
                "recent watch should have more influence due to recency decay with half-life of {0} days",
                _config.RecencyDecayHalfLifeDays);
        }

        [Fact]
        public void BuildUserProfile_NoWatchHistory_ReturnsNull()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // No user data setup - empty watch history

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().BeNull();
        }

        [Fact]
        public void BuildUserProfile_NullEmbeddings_ThrowsArgumentNullException()
        {
            // Arrange
            var metadata = new Dictionary<Guid, MediaItemMetadata>();

            // Act
            Action act = () => _service.BuildUserProfile(_testUserId, null!, _config);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("embeddings");
        }

        [Fact]
        public void BuildUserProfile_NullConfig_ThrowsArgumentNullException()
        {
            // Arrange
            var embeddings = new Dictionary<Guid, ItemEmbedding>();

            // Act
            Action act = () => _service.BuildUserProfile(_testUserId, embeddings, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("config");
        }

        [Fact]
        public void BuildUserProfile_EmptyEmbeddings_ThrowsArgumentException()
        {
            // Arrange
            var embeddings = new Dictionary<Guid, ItemEmbedding>();

            // Act
            Action act = () => _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("embeddings")
                .WithMessage("Embeddings dictionary cannot be empty*");
        }

        [Fact]
        public void BuildUserProfile_UserNotFound_ReturnsNull()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            var unknownUserId = Guid.NewGuid();

            // Setup: Return null for unknown user
            _mockUserManager.Setup(m => m.GetUserById(unknownUserId)).Returns((User?)null);

            // Act
            var profile = _service.BuildUserProfile(unknownUserId, embeddings, _config);

            // Assert
            profile.Should().BeNull("user not found means no watch history");
        }

        [Fact]
        public void BuildUserProfile_IgnoresUnplayedItems()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);

            // Setup 3 played items
            var playedItems = library.Take(3).ToList();
            SetupUserDataMocks(playedItems);

            // Setup 2 unplayed items
            foreach (var item in library.Skip(3).Take(2))
            {
                var mockItem = new Mock<BaseItem>();
                _mockLibraryManager.Setup(m => m.GetItemById(item.Id)).Returns(mockItem.Object);

                var userData = new UserItemData
                {
                    Key = item.Id.ToString(),
                    Played = false // Not played
                };
                _mockUserDataManager.Setup(m => m.GetUserData(_testUser, mockItem.Object))
                    .Returns(userData);
            }

            // Act
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);

            // Assert
            profile.Should().NotBeNull();
            profile!.WatchedItemCount.Should().Be(3, "only played items should be counted");
        }

        [Fact]
        public void BuildUserProfile_With100WatchedItems_CompletesUnder100ms()
        {
            // Arrange - create 100 items
            var library = CreateLargeLibrary(100);
            var embeddings = CreateEmbeddings(library);
            var metadata = library.ToDictionary(i => i.Id, i => i);
            SetupUserDataMocks(library);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var profile = _service.BuildUserProfile(_testUserId, embeddings, _config);
            stopwatch.Stop();

            // Assert
            profile.Should().NotBeNull();
            profile!.WatchedItemCount.Should().Be(100);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
                "acceptance criteria requires <100ms for 100 watched items");
        }

        // Helper methods

        private List<MediaItemMetadata> CreateLargeLibrary(int itemCount)
        {
            var library = new List<MediaItemMetadata>();
            var genres = new[] { "Action", "Drama", "Comedy", "Sci-Fi", "Thriller" };
            var actors = new[] { "Actor A", "Actor B", "Actor C", "Actor D" };
            var directors = new[] { "Director X", "Director Y", "Director Z" };

            for (int i = 0; i < itemCount; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie)
                {
                    ReleaseYear = 1990 + (i % 30),
                    CommunityRating = 5.0f + (i % 5),
                    CriticRating = 50f + (i % 50)
                };

                item.AddGenre(genres[i % genres.Length]);
                item.AddActor(actors[i % actors.Length]);
                item.AddDirector(directors[i % directors.Length]);

                library.Add(item);
            }

            return library;
        }

        private Dictionary<Guid, ItemEmbedding> CreateEmbeddings(List<MediaItemMetadata> library)
        {
            var embeddings = new Dictionary<Guid, ItemEmbedding>();
            var dimension = 100; // Test dimension

            foreach (var item in library)
            {
                var vector = new float[dimension];
                // Create a simple test vector (just use item index for variation)
                for (int i = 0; i < dimension; i++)
                {
                    vector[i] = (float)Math.Sin(i + item.Name.GetHashCode() % 100);
                }

                // Normalize
                var magnitude = (float)Math.Sqrt(vector.Sum(x => x * x));
                if (magnitude > 0)
                {
                    for (int i = 0; i < dimension; i++)
                    {
                        vector[i] /= magnitude;
                    }
                }

                embeddings[item.Id] = new ItemEmbedding(item.Id, vector);
            }

            return embeddings;
        }

        private void SetupUserDataMocks(List<MediaItemMetadata> watchedItems)
        {
            foreach (var item in watchedItems)
            {
                SetupSpecificUserData(item, isFavorite: false, playCount: 1, daysAgo: 30);
            }
        }

        private void SetupSpecificUserData(
            MediaItemMetadata item,
            bool isFavorite,
            int playCount,
            int daysAgo)
        {
            // Create a minimal mock BaseItem - we can't mock Id since it's not virtual
            // So we'll just return any BaseItem and match on it being called with that item ID
            var mockItem = new Mock<BaseItem>();

            _mockLibraryManager.Setup(m => m.GetItemById(item.Id)).Returns(mockItem.Object);

            // Create actual UserItemData instance - properties are not virtual so can't be mocked
            var userData = new UserItemData
            {
                Key = item.Id.ToString(), // Required property
                Played = true,
                IsFavorite = isFavorite,
                PlayCount = playCount,
                LastPlayedDate = DateTime.UtcNow.AddDays(-daysAgo)
            };

            // Match on the specific mock item returned by GetItemById
            _mockUserDataManager.Setup(m => m.GetUserData(_testUser, mockItem.Object))
                .Returns(userData);
        }
    }
}
