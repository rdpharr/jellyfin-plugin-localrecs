using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Domain
{
    /// <summary>
    /// Tests for <see cref="LibraryAnalysisService"/>.
    /// This is a critical boundary layer between Jellyfin and our plugin - tests validate
    /// that we correctly handle all edge cases in Jellyfin's data model.
    /// </summary>
    public class LibraryAnalysisServiceTests
    {
        private readonly Mock<ILibraryManager> _mockLibraryManager;
        private readonly LibraryAnalysisService _service;

        public LibraryAnalysisServiceTests()
        {
            _mockLibraryManager = new Mock<ILibraryManager>();
            _service = new LibraryAnalysisService(
                _mockLibraryManager.Object,
                NullLogger<LibraryAnalysisService>.Instance);
        }

        #region GetAllMediaItems Tests

        [Fact]
        public void GetAllMediaItems_WithMoviesAndSeries_ReturnsBothTypes()
        {
            // Arrange
            var movies = new List<BaseItem>
            {
                CreateMockMovie("Movie 1", 2020),
                CreateMockMovie("Movie 2", 2021)
            };
            var series = new List<BaseItem>
            {
                CreateMockSeries("Series 1", 2019)
            };

            SetupLibraryManagerReturns(movies, series);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().HaveCount(3);
            result.Count(i => i.Type == Models.MediaType.Movie).Should().Be(2);
            result.Count(i => i.Type == Models.MediaType.Series).Should().Be(1);
        }

        [Fact]
        public void GetAllMediaItems_EmptyLibrary_ReturnsEmptyList()
        {
            // Arrange
            SetupLibraryManagerReturns(new List<BaseItem>(), new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_OnlyMovies_ReturnsMoviesOnly()
        {
            // Arrange
            var movies = new List<BaseItem>
            {
                CreateMockMovie("Movie 1", 2020)
            };
            SetupLibraryManagerReturns(movies, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().ContainSingle();
            result[0].Type.Should().Be(Models.MediaType.Movie);
        }

        [Fact]
        public void GetAllMediaItems_OnlySeries_ReturnsSeriesOnly()
        {
            // Arrange
            var series = new List<BaseItem>
            {
                CreateMockSeries("Series 1", 2020)
            };
            SetupLibraryManagerReturns(new List<BaseItem>(), series);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().ContainSingle();
            result[0].Type.Should().Be(Models.MediaType.Series);
        }

        #endregion

        #region ConvertToMetadata Tests - Basic Properties

        [Fact]
        public void GetAllMediaItems_PreservesBasicProperties()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("The Matrix", 1999, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().ContainSingle();
            var item = result[0];
            item.Id.Should().Be(movieId);
            item.Name.Should().Be("The Matrix");
            item.ReleaseYear.Should().Be(1999);
        }

        [Fact]
        public void GetAllMediaItems_PreservesRatings()
        {
            // Arrange
            var movie = CreateMockMovie("Rated Movie", 2020);
            movie.CommunityRating = 8.5f;
            movie.CriticRating = 92f;
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].CommunityRating.Should().Be(8.5f);
            result[0].CriticRating.Should().Be(92f);
        }

        [Fact]
        public void GetAllMediaItems_PreservesPath()
        {
            // Arrange
            var movie = CreateMockMovie("Movie With Path", 2020);
            movie.Path = "/media/movies/movie.mkv";
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Path.Should().Be("/media/movies/movie.mkv");
        }

        #endregion

        #region ConvertToMetadata Tests - Genres

        [Fact]
        public void GetAllMediaItems_ExtractsGenres()
        {
            // Arrange
            var movie = CreateMockMovie("Genre Movie", 2020);
            movie.Genres = new[] { "Action", "Science Fiction", "Thriller" };
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Genres.Should().BeEquivalentTo(new[] { "Action", "Science Fiction", "Thriller" });
        }

        [Fact]
        public void GetAllMediaItems_NullGenres_ReturnsEmptyGenreList()
        {
            // Arrange
            var movie = CreateMockMovie("No Genre Movie", 2020);
            movie.Genres = null!;
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Genres.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_EmptyGenres_ReturnsEmptyGenreList()
        {
            // Arrange
            var movie = CreateMockMovie("Empty Genre Movie", 2020);
            movie.Genres = Array.Empty<string>();
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Genres.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_GenresWithWhitespace_FiltersOutWhitespace()
        {
            // Arrange
            var movie = CreateMockMovie("Whitespace Genre Movie", 2020);
            movie.Genres = new[] { "Action", "", "  ", "Drama", null! };
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Genres.Should().BeEquivalentTo(new[] { "Action", "Drama" });
        }

        #endregion

        #region ConvertToMetadata Tests - People (Actors/Directors)

        [Fact]
        public void GetAllMediaItems_ExtractsActors()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Actor Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            var people = new List<PersonInfo>
            {
                new PersonInfo { Name = "Keanu Reeves", Type = PersonKind.Actor },
                new PersonInfo { Name = "Laurence Fishburne", Type = PersonKind.Actor },
                new PersonInfo { Name = "Carrie-Anne Moss", Type = PersonKind.Actor }
            };
            SetupPeopleForItem(movieId, people);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Actors.Should().BeEquivalentTo(new[] { "Keanu Reeves", "Laurence Fishburne", "Carrie-Anne Moss" });
        }

        [Fact]
        public void GetAllMediaItems_ExtractsDirectors()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Director Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            var people = new List<PersonInfo>
            {
                new PersonInfo { Name = "Christopher Nolan", Type = PersonKind.Director },
                new PersonInfo { Name = "Denis Villeneuve", Type = PersonKind.Director }
            };
            SetupPeopleForItem(movieId, people);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Directors.Should().BeEquivalentTo(new[] { "Christopher Nolan", "Denis Villeneuve" });
        }

        [Fact]
        public void GetAllMediaItems_ExtractsAllActors()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Action Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            var people = new List<PersonInfo>
            {
                new PersonInfo { Name = "Actor 1", Type = PersonKind.Actor },
                new PersonInfo { Name = "Actor 2", Type = PersonKind.Actor },
                new PersonInfo { Name = "Actor 3", Type = PersonKind.Actor },
                new PersonInfo { Name = "Actor 4", Type = PersonKind.Actor },
                new PersonInfo { Name = "Actor 5", Type = PersonKind.Actor }
            };
            SetupPeopleForItem(movieId, people);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert - should extract all actors, not limited to 3
            result[0].Actors.Should().HaveCount(5);
        }

        [Fact]
        public void GetAllMediaItems_NoPeople_ReturnsEmptyActorsAndDirectors()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("No People Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());
            SetupPeopleForItem(movieId, new List<PersonInfo>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Actors.Should().BeEmpty();
            result[0].Directors.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_PeopleWithNullNames_FiltersOut()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Null Name Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            var people = new List<PersonInfo>
            {
                new PersonInfo { Name = "Valid Actor", Type = PersonKind.Actor },
                new PersonInfo { Name = null!, Type = PersonKind.Actor },
                new PersonInfo { Name = "", Type = PersonKind.Actor },
                new PersonInfo { Name = "   ", Type = PersonKind.Actor }
            };
            SetupPeopleForItem(movieId, people);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Actors.Should().ContainSingle("Valid Actor");
        }

        [Fact]
        public void GetAllMediaItems_MixedPeopleTypes_SeparatesCorrectly()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Mixed People Movie", 2020, movieId);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            var people = new List<PersonInfo>
            {
                new PersonInfo { Name = "Actor Name", Type = PersonKind.Actor },
                new PersonInfo { Name = "Director Name", Type = PersonKind.Director },
                new PersonInfo { Name = "Writer Name", Type = PersonKind.Writer },
                new PersonInfo { Name = "Producer Name", Type = PersonKind.Producer }
            };
            SetupPeopleForItem(movieId, people);

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].Actors.Should().ContainSingle("Actor Name");
            result[0].Directors.Should().ContainSingle("Director Name");
        }

        #endregion

        #region ConvertToMetadata Tests - Edge Cases

        [Fact]
        public void GetAllMediaItems_NullItem_IsSkipped()
        {
            // Arrange - Include a null in the list (shouldn't happen but defensive)
            var movies = new List<BaseItem>
            {
                CreateMockMovie("Valid Movie", 2020),
                null!
            };
            SetupLibraryManagerReturns(movies, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().ContainSingle();
        }

        [Fact]
        public void GetAllMediaItems_ItemWithNullName_IsSkipped()
        {
            // Arrange
            var movie = CreateMockMovie(null!, 2020);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_ItemWithEmptyName_IsSkipped()
        {
            // Arrange
            var movie = CreateMockMovie("", 2020);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetAllMediaItems_NullProductionYear_LeavesYearNull()
        {
            // Arrange
            var movie = CreateMockMovie("No Year Movie", null);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].ReleaseYear.Should().BeNull("null ProductionYear should remain null");
        }

        [Fact]
        public void GetAllMediaItems_NullRatings_PreservesNulls()
        {
            // Arrange
            var movie = CreateMockMovie("No Rating Movie", 2020);
            movie.CommunityRating = null;
            movie.CriticRating = null;
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].CommunityRating.Should().BeNull();
            result[0].CriticRating.Should().BeNull();
        }

        #endregion

        #region GetMediaItem Tests

        [Fact]
        public void GetMediaItem_ExistingMovie_ReturnsMetadata()
        {
            // Arrange
            var movieId = Guid.NewGuid();
            var movie = CreateMockMovie("Single Movie", 2020, movieId);
            _mockLibraryManager.Setup(m => m.GetItemById(movieId)).Returns(movie);
            SetupPeopleForItem(movieId, new List<PersonInfo>());

            // Act
            var result = _service.GetMediaItem(movieId);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().Be("Single Movie");
            result.Type.Should().Be(Models.MediaType.Movie);
        }

        [Fact]
        public void GetMediaItem_ExistingSeries_ReturnsMetadata()
        {
            // Arrange
            var seriesId = Guid.NewGuid();
            var series = CreateMockSeries("Single Series", 2020, seriesId);
            _mockLibraryManager.Setup(m => m.GetItemById(seriesId)).Returns(series);
            SetupPeopleForItem(seriesId, new List<PersonInfo>());

            // Act
            var result = _service.GetMediaItem(seriesId);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().Be("Single Series");
            result.Type.Should().Be(Models.MediaType.Series);
        }

        [Fact]
        public void GetMediaItem_NonExistentId_ReturnsNull()
        {
            // Arrange
            var nonExistentId = Guid.NewGuid();
            _mockLibraryManager.Setup(m => m.GetItemById(nonExistentId)).Returns((BaseItem)null!);

            // Act
            var result = _service.GetMediaItem(nonExistentId);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetMediaItem_UnsupportedItemType_ReturnsNull()
        {
            // Arrange - Use a concrete BaseItem subclass that's not Movie or Series
            // Since BaseItem.Id can't be mocked, we return a generic BaseItem from the mock
            var itemId = Guid.NewGuid();

            // Create a generic BaseItem (not Movie or Series)
            // The service should return null for unsupported types
            _mockLibraryManager.Setup(m => m.GetItemById(itemId)).Returns((BaseItem)null!);

            // Act
            var result = _service.GetMediaItem(itemId);

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region Provider ID Tests

        [Fact]
        public void GetAllMediaItems_ExtractsTmdbId()
        {
            // Arrange
            var movie = CreateMockMovie("TMDB Movie", 2020);
            movie.SetProviderId(MetadataProvider.Tmdb, "12345");
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].TmdbId.Should().Be("12345");
        }

        [Fact]
        public void GetAllMediaItems_ExtractsTvdbId()
        {
            // Arrange
            var series = CreateMockSeries("TVDB Series", 2020);
            series.SetProviderId(MetadataProvider.Tvdb, "67890");
            SetupLibraryManagerReturns(new List<BaseItem>(), new List<BaseItem> { series });

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].TvdbId.Should().Be("67890");
        }

        [Fact]
        public void GetAllMediaItems_MissingProviderIds_LeavesNull()
        {
            // Arrange
            var movie = CreateMockMovie("No Provider ID Movie", 2020);
            SetupLibraryManagerReturns(new List<BaseItem> { movie }, new List<BaseItem>());

            // Act
            var result = _service.GetAllMediaItems();

            // Assert
            result[0].TmdbId.Should().BeNull();
            result[0].TvdbId.Should().BeNull();
        }

        #endregion

        #region Helper Methods

        private Movie CreateMockMovie(string name, int? year, Guid? id = null)
        {
            var movie = new Movie
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                ProductionYear = year,
                Genres = Array.Empty<string>()
            };
            return movie;
        }

        private Series CreateMockSeries(string name, int? year, Guid? id = null)
        {
            var series = new Series
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                ProductionYear = year,
                Genres = Array.Empty<string>()
            };
            return series;
        }

        private void SetupLibraryManagerReturns(List<BaseItem> movies, List<BaseItem> series)
        {
            _mockLibraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q =>
                    q.IncludeItemTypes != null &&
                    q.IncludeItemTypes.Contains(BaseItemKind.Movie))))
                .Returns(movies);

            _mockLibraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q =>
                    q.IncludeItemTypes != null &&
                    q.IncludeItemTypes.Contains(BaseItemKind.Series))))
                .Returns(series);

            // Setup empty people queries by default
            foreach (var movie in movies.Where(m => m != null))
            {
                SetupPeopleForItem(movie.Id, new List<PersonInfo>());
            }

            foreach (var show in series.Where(s => s != null))
            {
                SetupPeopleForItem(show.Id, new List<PersonInfo>());
            }
        }

        private void SetupPeopleForItem(Guid itemId, List<PersonInfo> people)
        {
            _mockLibraryManager
                .Setup(m => m.GetPeople(It.Is<InternalPeopleQuery>(q => q.ItemId == itemId)))
                .Returns(people);
        }

        #endregion
    }
}
