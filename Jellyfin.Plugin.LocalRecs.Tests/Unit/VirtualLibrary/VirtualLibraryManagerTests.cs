using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.VirtualLibrary
{
    /// <summary>
    /// Tests for VirtualLibraryManager file operations.
    /// </summary>
    public class VirtualLibraryManagerTests : IDisposable
    {
        private readonly string _testBasePath;
        private readonly VirtualLibraryManager _manager;
        private readonly Mock<ILibraryManager> _mockLibraryManager;

        public VirtualLibraryManagerTests()
        {
            // Create a temp directory for testing
            _testBasePath = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testBasePath);

            _mockLibraryManager = new Mock<ILibraryManager>();

            _manager = new VirtualLibraryManager(
                NullLogger<VirtualLibraryManager>.Instance,
                _mockLibraryManager.Object,
                _testBasePath);
        }

        public void Dispose()
        {
            // Cleanup test directory
            if (Directory.Exists(_testBasePath))
            {
                Directory.Delete(_testBasePath, recursive: true);
            }

            GC.SuppressFinalize(this);
        }

        [Fact]
        public void EnsureUserDirectoriesExist_CreatesMovieAndTvDirectories()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var username = "TestUser";

            // Act
            var result = _manager.EnsureUserDirectoriesExist(userId, username);

            // Assert
            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_testBasePath, userId.ToString(), "movies")).Should().BeTrue();
            Directory.Exists(Path.Combine(_testBasePath, userId.ToString(), "tv")).Should().BeTrue();
        }

        [Fact]
        public void GetUserLibraryPath_ReturnsCorrectMoviePath()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            // Act
            var path = _manager.GetUserLibraryPath(userId, MediaType.Movie);

            // Assert
            path.Should().Be(Path.Combine(_testBasePath, userId.ToString(), "movies"));
        }

        [Fact]
        public void GetUserLibraryPath_ReturnsCorrectTvPath()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            // Act
            var path = _manager.GetUserLibraryPath(userId, MediaType.Series);

            // Assert
            path.Should().Be(Path.Combine(_testBasePath, userId.ToString(), "tv"));
        }

        [Fact]
        public void SyncRecommendations_CreatesStrmFileForMovie()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Test Movie",
                Path = "/media/movies/TestMovie.mkv",
                ProductionYear = 2023
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            var recommendations = new[]
            {
                new ScoredRecommendation(movieId, 0.95f)
            };

            // Act
            _manager.SyncRecommendations(userId, recommendations, MediaType.Movie);

            // Assert - Movies are now in folders with .strm and optional trailer files
            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var movieFolders = Directory.GetDirectories(moviePath);
            movieFolders.Should().HaveCount(1);

            // Check for .strm file inside the movie folder
            var strmFiles = Directory.GetFiles(movieFolders[0], "*.strm");
            strmFiles.Should().HaveCountGreaterOrEqualTo(1);

            // Check the main .strm file content (not the trailer)
            var mainStrmFile = strmFiles.First(f => !f.Contains("-trailer"));
            var strmContent = File.ReadAllText(mainStrmFile);
            strmContent.Should().Be("/media/movies/TestMovie.mkv");
        }

        [Fact]
        public void SyncRecommendations_ClearsOldRecommendationsBeforeCreatingNew()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movieId1 = Guid.NewGuid();
            var movieId2 = Guid.NewGuid();

            var mockMovie1 = new Movie
            {
                Id = movieId1,
                Name = "Movie 1",
                Path = "/media/movies/Movie1.mkv",
                ProductionYear = 2023
            };

            var mockMovie2 = new Movie
            {
                Id = movieId2,
                Name = "Movie 2",
                Path = "/media/movies/Movie2.mkv",
                ProductionYear = 2024
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId1)).Returns(mockMovie1);
            _mockLibraryManager.Setup(m => m.GetItemById(movieId2)).Returns(mockMovie2);

            // First sync
            var firstRecommendations = new[]
            {
                new ScoredRecommendation(movieId1, 0.95f)
            };
            _manager.SyncRecommendations(userId, firstRecommendations, MediaType.Movie);

            // Act - Second sync with different recommendations
            var secondRecommendations = new[]
            {
                new ScoredRecommendation(movieId2, 0.90f)
            };
            _manager.SyncRecommendations(userId, secondRecommendations, MediaType.Movie);

            // Assert - Should only have Movie 2 folder, not Movie 1
            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var movieFolders = Directory.GetDirectories(moviePath);
            movieFolders.Should().HaveCount(1);

            // Check the folder name contains Movie 2
            movieFolders[0].Should().Contain("Movie 2");

            // Check the main .strm file content
            var strmFiles = Directory.GetFiles(movieFolders[0], "*.strm")
                .Where(f => !f.Contains("-trailer")).ToArray();
            strmFiles.Should().HaveCount(1);

            var strmContent = File.ReadAllText(strmFiles[0]);
            strmContent.Should().Be("/media/movies/Movie2.mkv");
        }

        [Fact]
        public void DeleteUserDirectories_RemovesUserDirectory()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var userDir = Path.Combine(_testBasePath, userId.ToString());
            Directory.Exists(userDir).Should().BeTrue();

            // Act
            _manager.DeleteUserDirectories(userId);

            // Assert
            Directory.Exists(userDir).Should().BeFalse();
        }

        [Fact]
        public void SyncRecommendations_HandlesEmptyRecommendationsList()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            // Act
            _manager.SyncRecommendations(userId, Array.Empty<ScoredRecommendation>(), MediaType.Movie);

            // Assert - Directory should exist but be empty
            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            Directory.Exists(moviePath).Should().BeTrue();
            var strmFiles = Directory.GetFiles(moviePath, "*.strm");
            strmFiles.Should().BeEmpty();
        }

        [Fact]
        public void SanitizeFilename_RemovesInvalidCharacters()
        {
            // This is a white-box test - we can't directly call SanitizeFilename,
            // but we can verify it works through a movie with special characters in the name
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Test: Movie / With \\ Special | Characters",
                Path = "/media/movies/TestMovie.mkv",
                ProductionYear = 2023
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            var recommendations = new[]
            {
                new ScoredRecommendation(movieId, 0.95f)
            };

            // Act
            _manager.SyncRecommendations(userId, recommendations, MediaType.Movie);

            // Assert - Should create a folder without invalid characters
            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var movieFolders = Directory.GetDirectories(moviePath);
            movieFolders.Should().HaveCount(1);

            // Folder name should not contain invalid characters
            var folderName = Path.GetFileName(movieFolders[0]);
            folderName.Should().NotContain(":");
            folderName.Should().NotContain("/");
            folderName.Should().NotContain("\\");
            folderName.Should().NotContain("|");

            // .strm file should exist inside the folder
            var strmFiles = Directory.GetFiles(movieFolders[0], "*.strm")
                .Where(f => !f.Contains("-trailer")).ToArray();
            strmFiles.Should().HaveCount(1);
        }
    }
}
