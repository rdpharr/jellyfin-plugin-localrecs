using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.VirtualLibrary
{
    /// <summary>
    /// Tests for VirtualLibraryManager symlink-based virtual library creation.
    /// Symlink creation on Windows requires Developer Mode or Administrator — tests
    /// that exercise real symlinks are gated on that capability.
    /// </summary>
    public class VirtualLibraryManagerTests : IDisposable
    {
        private readonly string _testBasePath;
        private readonly string _sourceMediaDir;
        private readonly string _sourceMediaFile;
        private readonly VirtualLibraryManager _manager;
        private readonly Mock<ILibraryManager> _mockLibraryManager;

        public VirtualLibraryManagerTests()
        {
            _testBasePath = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testBasePath);

            // Real source file so File.CreateSymbolicLink has something to point at
            _sourceMediaDir = Path.Combine(_testBasePath, "source", "TestMovie");
            Directory.CreateDirectory(_sourceMediaDir);
            _sourceMediaFile = Path.Combine(_sourceMediaDir, "TestMovie.mkv");
            File.WriteAllText(_sourceMediaFile, "fake mkv content");

            _mockLibraryManager = new Mock<ILibraryManager>();

            _manager = new VirtualLibraryManager(
                NullLogger<VirtualLibraryManager>.Instance,
                _mockLibraryManager.Object,
                _testBasePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testBasePath))
            {
                try
                {
                    Directory.Delete(_testBasePath, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            GC.SuppressFinalize(this);
        }

        private static bool CanCreateSymlinks()
        {
            // Attempt to create a test symlink; if it fails (e.g. Windows without
            // Developer Mode), skip tests that require real symlinks.
            var probe = Path.Combine(Path.GetTempPath(), "jf-localrecs-symlink-probe-" + Guid.NewGuid());
            var target = Path.Combine(Path.GetTempPath(), "jf-localrecs-symlink-target-" + Guid.NewGuid());
            try
            {
                File.WriteAllText(target, "x");
                File.CreateSymbolicLink(probe, target);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(probe))
                    {
                        File.Delete(probe);
                    }

                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Fact]
        public void EnsureUserDirectoriesExist_CreatesMovieAndTvDirectories()
        {
            var userId = Guid.NewGuid();
            var result = _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_testBasePath, userId.ToString(), "movies")).Should().BeTrue();
            Directory.Exists(Path.Combine(_testBasePath, userId.ToString(), "tv")).Should().BeTrue();
        }

        [Fact]
        public void GetUserLibraryPath_ReturnsCorrectMoviePath()
        {
            var userId = Guid.NewGuid();
            var path = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            path.Should().Be(Path.Combine(_testBasePath, userId.ToString(), "movies"));
        }

        [Fact]
        public void GetUserLibraryPath_ReturnsCorrectTvPath()
        {
            var userId = Guid.NewGuid();
            var path = _manager.GetUserLibraryPath(userId, MediaType.Series);
            path.Should().Be(Path.Combine(_testBasePath, userId.ToString(), "tv"));
        }

        [Fact]
        public void SyncRecommendations_CreatesSymlinkToSourceMedia()
        {
            if (!CanCreateSymlinks())
            {
                return;
            }

            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Test Movie",
                Path = _sourceMediaFile,
                ProductionYear = 2023
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            var recommendations = new[] { new ScoredRecommendation(movieId, 0.95f) };
            _manager.SyncRecommendations(userId, recommendations, MediaType.Movie);

            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var movieFolders = Directory.GetDirectories(moviePath);
            movieFolders.Should().HaveCount(1);

            var mediaLinks = Directory.GetFiles(movieFolders[0], "*.mkv");
            mediaLinks.Should().HaveCount(1);

            // Confirm it's actually a symlink pointing at the source
            var info = new FileInfo(mediaLinks[0]);
            info.LinkTarget.Should().NotBeNull();
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            target!.FullName.Should().Be(new FileInfo(_sourceMediaFile).FullName);
        }

        [Fact]
        public void SyncRecommendations_PreservesSourceFileExtension()
        {
            if (!CanCreateSymlinks())
            {
                return;
            }

            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var mp4Source = Path.Combine(_sourceMediaDir, "Other.mp4");
            File.WriteAllText(mp4Source, "fake mp4");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Other Movie",
                Path = mp4Source,
                ProductionYear = 2024
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            _manager.SyncRecommendations(
                userId,
                new[] { new ScoredRecommendation(movieId, 0.9f) },
                MediaType.Movie);

            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var folder = Directory.GetDirectories(moviePath).Single();
            Directory.GetFiles(folder, "*.mp4").Should().HaveCount(1);
            Directory.GetFiles(folder, "*.strm").Should().BeEmpty();
        }

        [Fact]
        public void SyncRecommendations_SymlinksArtworkFromSourceFolder()
        {
            if (!CanCreateSymlinks())
            {
                return;
            }

            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            // Drop artwork in source folder
            var posterPath = Path.Combine(_sourceMediaDir, "poster.jpg");
            var fanartPath = Path.Combine(_sourceMediaDir, "fanart.jpg");
            File.WriteAllText(posterPath, "fake poster");
            File.WriteAllText(fanartPath, "fake fanart");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Art Movie",
                Path = _sourceMediaFile,
                ProductionYear = 2023,
                ImageInfos = new[]
                {
                    new ItemImageInfo { Type = ImageType.Primary, Path = posterPath },
                    new ItemImageInfo { Type = ImageType.Backdrop, Path = fanartPath }
                }
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            _manager.SyncRecommendations(
                userId,
                new[] { new ScoredRecommendation(movieId, 0.9f) },
                MediaType.Movie);

            var folder = Directory.GetDirectories(_manager.GetUserLibraryPath(userId, MediaType.Movie)).Single();
            File.Exists(Path.Combine(folder, "poster.jpg")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "fanart.jpg")).Should().BeTrue();
            new FileInfo(Path.Combine(folder, "poster.jpg")).LinkTarget.Should().NotBeNull();
        }

        [Fact]
        public void SyncRecommendations_ClearsOldRecommendationsBeforeCreatingNew()
        {
            if (!CanCreateSymlinks())
            {
                return;
            }

            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movie1Source = Path.Combine(_sourceMediaDir, "Movie1.mkv");
            var movie2Source = Path.Combine(_sourceMediaDir, "Movie2.mkv");
            File.WriteAllText(movie1Source, "x");
            File.WriteAllText(movie2Source, "x");

            var movieId1 = Guid.NewGuid();
            var movieId2 = Guid.NewGuid();

            _mockLibraryManager.Setup(m => m.GetItemById(movieId1)).Returns(new Movie
            {
                Id = movieId1,
                Name = "Movie 1",
                Path = movie1Source,
                ProductionYear = 2023
            });
            _mockLibraryManager.Setup(m => m.GetItemById(movieId2)).Returns(new Movie
            {
                Id = movieId2,
                Name = "Movie 2",
                Path = movie2Source,
                ProductionYear = 2024
            });

            _manager.SyncRecommendations(userId, new[] { new ScoredRecommendation(movieId1, 0.95f) }, MediaType.Movie);
            _manager.SyncRecommendations(userId, new[] { new ScoredRecommendation(movieId2, 0.90f) }, MediaType.Movie);

            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            var movieFolders = Directory.GetDirectories(moviePath);
            movieFolders.Should().HaveCount(1);
            movieFolders[0].Should().Contain("Movie 2");
        }

        [Fact]
        public void DeleteUserDirectories_RemovesUserDirectory()
        {
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var userDir = Path.Combine(_testBasePath, userId.ToString());
            Directory.Exists(userDir).Should().BeTrue();

            _manager.DeleteUserDirectories(userId);
            Directory.Exists(userDir).Should().BeFalse();
        }

        [Fact]
        public void SyncRecommendations_HandlesEmptyRecommendationsList()
        {
            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            _manager.SyncRecommendations(userId, Array.Empty<ScoredRecommendation>(), MediaType.Movie);

            var moviePath = _manager.GetUserLibraryPath(userId, MediaType.Movie);
            Directory.Exists(moviePath).Should().BeTrue();
            Directory.GetFileSystemEntries(moviePath).Should().BeEmpty();
        }

        [Fact]
        public void SanitizeFilename_RemovesInvalidCharactersFromFolderName()
        {
            if (!CanCreateSymlinks())
            {
                return;
            }

            var userId = Guid.NewGuid();
            _manager.EnsureUserDirectoriesExist(userId, "TestUser");

            var movieId = Guid.NewGuid();
            var mockMovie = new Movie
            {
                Id = movieId,
                Name = "Test: Movie / With \\ Special | Characters",
                Path = _sourceMediaFile,
                ProductionYear = 2023
            };

            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(mockMovie);

            _manager.SyncRecommendations(
                userId,
                new[] { new ScoredRecommendation(movieId, 0.95f) },
                MediaType.Movie);

            var movieFolders = Directory.GetDirectories(_manager.GetUserLibraryPath(userId, MediaType.Movie));
            movieFolders.Should().HaveCount(1);

            var folderName = Path.GetFileName(movieFolders[0]);
            folderName.Should().NotContain(":");
            folderName.Should().NotContain("|");
        }
    }
}
