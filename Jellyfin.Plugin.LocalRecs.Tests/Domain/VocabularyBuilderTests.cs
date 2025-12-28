using System;
using System.Collections.Generic;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Domain
{
    /// <summary>
    /// Tests for <see cref="VocabularyBuilder"/>.
    /// Validates vocabulary construction, IDF computation, and vocabulary limiting.
    /// </summary>
    public class VocabularyBuilderTests
    {
        private readonly VocabularyBuilder _builder;

        public VocabularyBuilderTests()
        {
            _builder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
        }

        #region Basic Vocabulary Building

        [Fact]
        public void BuildVocabulary_WithTestLibrary_ExtractsAllGenres()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Genres.Should().NotBeEmpty();
            vocabulary.Genres.Keys.Should().Contain("Science Fiction");
            vocabulary.Genres.Keys.Should().Contain("Drama");
            vocabulary.Genres.Keys.Should().Contain("Action");
            vocabulary.Genres.Keys.Should().Contain("Comedy");
        }

        [Fact]
        public void BuildVocabulary_WithTestLibrary_ExtractsAllActors()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Actors.Should().NotBeEmpty();
            vocabulary.Actors.Keys.Should().Contain("Keanu Reeves");
            vocabulary.Actors.Keys.Should().Contain("Leonardo DiCaprio");
        }

        [Fact]
        public void BuildVocabulary_WithTestLibrary_ExtractsAllDirectors()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Directors.Should().NotBeEmpty();
            vocabulary.Directors.Keys.Should().Contain("Christopher Nolan");
            vocabulary.Directors.Keys.Should().Contain("Ridley Scott");
        }

        [Fact]
        public void BuildVocabulary_SetsTotalItemsCorrectly()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.TotalItems.Should().Be(library.Count);
        }

        [Fact]
        public void BuildVocabulary_ExtractsAllDecades()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Decades.Should().NotBeEmpty();
            vocabulary.Decades.Keys.Should().Contain("1970s"); // Alien (1979)
            vocabulary.Decades.Keys.Should().Contain("1990s"); // The Matrix (1999)
            vocabulary.Decades.Keys.Should().Contain("2010s"); // Blade Runner 2049 (2017)
        }

        #endregion

        #region IDF Computation

        [Fact]
        public void BuildVocabulary_CommonGenre_HasLowerIdf()
        {
            // Arrange - Create library where "Action" is common, "Animation" is rare
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddGenre("Action"); // All have Action
                if (i == 0)
                {
                    item.AddGenre("Animation"); // Only one has Animation
                }

                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var actionIdf = vocabulary.GenreIdf["Action"];
            var animationIdf = vocabulary.GenreIdf["Animation"];

            actionIdf.Should().BeLessThan(animationIdf,
                "common genre (Action) should have lower IDF than rare genre (Animation)");
        }

        [Fact]
        public void BuildVocabulary_UniqueFeature_HasHighestIdf()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddActor("Common Actor");
                if (i == 0)
                {
                    item.AddActor("Unique Actor");
                }

                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var commonIdf = vocabulary.ActorIdf["Common Actor"];
            var uniqueIdf = vocabulary.ActorIdf["Unique Actor"];

            uniqueIdf.Should().BeGreaterThan(commonIdf);
            uniqueIdf.Should().BeApproximately((float)Math.Log(10.0 / 1.0), 0.01f);
        }

        [Fact]
        public void BuildVocabulary_AllItemsHaveFeature_IdfIsZero()
        {
            // Arrange - All items have the same genre
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 5; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddGenre("Universal Genre");
                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var idf = vocabulary.GenreIdf["Universal Genre"];
            idf.Should().BeApproximately(0f, 0.01f, "IDF should be 0 when all items have the feature");
        }

        #endregion

        #region Vocabulary Limiting

        [Fact]
        public void BuildVocabulary_WithMaxActors_LimitsVocabularySize()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 20; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddActor($"Actor {i}"); // Each movie has unique actor
                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library, maxActors: 5);

            // Assert
            vocabulary.Actors.Count.Should().Be(5);
        }

        [Fact]
        public void BuildVocabulary_WithMaxDirectors_LimitsVocabularySize()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 20; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddDirector($"Director {i}");
                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library, maxDirectors: 3);

            // Assert
            vocabulary.Directors.Count.Should().Be(3);
        }

        [Fact]
        public void BuildVocabulary_WithMaxActors_KeepsMostCommonActors()
        {
            // Arrange - Create library where some actors appear more frequently
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);

                // "Popular Actor" appears in all movies
                item.AddActor("Popular Actor");

                // "Common Actor" appears in 5 movies
                if (i < 5)
                {
                    item.AddActor("Common Actor");
                }

                // Unique actors
                item.AddActor($"Unique Actor {i}");

                library.Add(item);
            }

            // Act - Limit to 2 actors
            var vocabulary = _builder.BuildVocabulary(library, maxActors: 2);

            // Assert - Should keep the most common actors
            vocabulary.Actors.Keys.Should().Contain("Popular Actor");
            vocabulary.Actors.Keys.Should().Contain("Common Actor");
            vocabulary.Actors.Keys.Should().NotContain("Unique Actor 0");
        }

        [Fact]
        public void BuildVocabulary_ZeroMaxActors_KeepsAllActors()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddActor($"Actor {i}");
                library.Add(item);
            }

            // Act - maxActors = 0 means unlimited
            var vocabulary = _builder.BuildVocabulary(library, maxActors: 0);

            // Assert
            vocabulary.Actors.Count.Should().Be(10);
        }

        [Fact]
        public void BuildVocabulary_WithMaxTags_LimitsVocabularySize()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 20; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddTag($"Tag {i}"); // Each movie has unique tag
                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library, maxTags: 5);

            // Assert
            vocabulary.Tags.Count.Should().Be(5);
        }

        [Fact]
        public void BuildVocabulary_WithMaxTags_KeepsMostCommonTags()
        {
            // Arrange - Create library where some tags appear more frequently
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);

                // "Popular Tag" appears in all movies
                item.AddTag("Popular Tag");

                // "Common Tag" appears in 5 movies
                if (i < 5)
                {
                    item.AddTag("Common Tag");
                }

                // Unique tags
                item.AddTag($"Unique Tag {i}");

                library.Add(item);
            }

            // Act - Limit to 2 tags
            var vocabulary = _builder.BuildVocabulary(library, maxTags: 2);

            // Assert - Should keep the most common tags
            vocabulary.Tags.Keys.Should().Contain("Popular Tag");
            vocabulary.Tags.Keys.Should().Contain("Common Tag");
            vocabulary.Tags.Keys.Should().NotContain("Unique Tag 0");
        }

        [Fact]
        public void BuildVocabulary_ZeroMaxTags_KeepsAllTags()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddTag($"Tag {i}");
                library.Add(item);
            }

            // Act - maxTags = 0 means unlimited
            var vocabulary = _builder.BuildVocabulary(library, maxTags: 0);

            // Assert
            vocabulary.Tags.Count.Should().Be(10);
        }

        [Fact]
        public void BuildVocabulary_CommonTag_HasLowerIdf()
        {
            // Arrange - Create library where some tags appear more frequently
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.AddTag("Common Tag"); // All have this
                if (i == 0)
                {
                    item.AddTag("Rare Tag"); // Only one has this
                }

                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var commonIdf = vocabulary.TagIdf["Common Tag"];
            var rareIdf = vocabulary.TagIdf["Rare Tag"];

            commonIdf.Should().BeLessThan(rareIdf,
                "common tag should have lower IDF than rare tag");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void BuildVocabulary_EmptyLibrary_ReturnsEmptyVocabulary()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Genres.Should().BeEmpty();
            vocabulary.Actors.Should().BeEmpty();
            vocabulary.Directors.Should().BeEmpty();
            vocabulary.TotalItems.Should().Be(0);
        }

        [Fact]
        public void BuildVocabulary_NullItems_ThrowsArgumentNullException()
        {
            // Act & Assert
            var action = () => _builder.BuildVocabulary(null!);
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void BuildVocabulary_ItemsWithNoGenres_HandlesGracefully()
        {
            // Arrange
            var library = new List<MediaItemMetadata>
            {
                new MediaItemMetadata(Guid.NewGuid(), "Movie 1", MediaType.Movie),
                new MediaItemMetadata(Guid.NewGuid(), "Movie 2", MediaType.Movie)
            };

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Genres.Should().BeEmpty();
            vocabulary.TotalItems.Should().Be(2);
        }

        [Fact]
        public void BuildVocabulary_CaseInsensitiveGenres_TreatsAsSameFeature()
        {
            // Arrange
            var library = new List<MediaItemMetadata>();

            var item1 = new MediaItemMetadata(Guid.NewGuid(), "Movie 1", MediaType.Movie);
            item1.AddGenre("ACTION");

            var item2 = new MediaItemMetadata(Guid.NewGuid(), "Movie 2", MediaType.Movie);
            item2.AddGenre("action");

            var item3 = new MediaItemMetadata(Guid.NewGuid(), "Movie 3", MediaType.Movie);
            item3.AddGenre("Action");

            library.Add(item1);
            library.Add(item2);
            library.Add(item3);

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert - Should only have one "action" entry (case-insensitive)
            vocabulary.Genres.Count.Should().Be(1);
        }

        [Fact]
        public void BuildVocabulary_DuplicateGenresInSingleItem_CountsOnce()
        {
            // Arrange - Item with duplicate genre (shouldn't happen but defensive)
            var item = new MediaItemMetadata(Guid.NewGuid(), "Movie", MediaType.Movie);
            item.AddGenre("Action");
            item.AddGenre("Action"); // Duplicate

            var library = new List<MediaItemMetadata> { item };

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Genres.Count.Should().Be(1);
            // IDF should be log(1/1) = 0 since it appears in all (1) documents
            vocabulary.GenreIdf["Action"].Should().BeApproximately(0f, 0.01f);
        }

        [Fact]
        public void BuildVocabulary_ItemsWithoutReleaseYear_GetUnknownDecade()
        {
            // Arrange
            var library = new List<MediaItemMetadata>
            {
                new MediaItemMetadata(Guid.NewGuid(), "Movie 1", MediaType.Movie) { ReleaseYear = 2000 },
                new MediaItemMetadata(Guid.NewGuid(), "Movie 2", MediaType.Movie) { ReleaseYear = null },
                new MediaItemMetadata(Guid.NewGuid(), "Movie 3", MediaType.Movie) { ReleaseYear = 2020 }
            };

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.Decades.Keys.Should().Contain("Unknown");
            vocabulary.Decades.Keys.Should().Contain("2000s");
            vocabulary.Decades.Keys.Should().Contain("2020s");
            vocabulary.Decades["Unknown"].Should().Be(1); // One item without ReleaseYear
        }

        [Fact]
        public void BuildVocabulary_CommonDecade_HasLowerIdf()
        {
            // Arrange - Create library where "2000s" is common, "1980s" is rare
            var library = new List<MediaItemMetadata>();
            for (int i = 0; i < 10; i++)
            {
                var item = new MediaItemMetadata(Guid.NewGuid(), $"Movie {i}", MediaType.Movie);
                item.ReleaseYear = 2000 + i; // All in 2000s

                if (i == 0)
                {
                    item.ReleaseYear = 1985; // Only one in 1980s
                }

                library.Add(item);
            }

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var decade2000sIdf = vocabulary.DecadeIdf["2000s"];
            var decade1980sIdf = vocabulary.DecadeIdf["1980s"];

            decade2000sIdf.Should().BeLessThan(decade1980sIdf,
                "common decade (2000s) should have lower IDF than rare decade (1980s)");
        }

        [Fact]
        public void BuildVocabulary_SingleItem_HasCorrectIdfValues()
        {
            // Arrange
            var item = new MediaItemMetadata(Guid.NewGuid(), "Single Movie", MediaType.Movie);
            item.AddGenre("Only Genre");
            item.AddActor("Only Actor");

            var library = new List<MediaItemMetadata> { item };

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert - IDF = log(1/1) = 0 for features that appear in all (1) documents
            vocabulary.GenreIdf["Only Genre"].Should().BeApproximately(0f, 0.01f);
            vocabulary.ActorIdf["Only Actor"].Should().BeApproximately(0f, 0.01f);
        }

        #endregion

        #region Integration with Real Test Data

        [Fact]
        public void BuildVocabulary_RealTestLibrary_ProducesValidDimensions()
        {
            // Arrange
            var library = TestMediaLibrary.CreateTestLibrary();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            vocabulary.TotalFeatures.Should().BeGreaterThan(0);
            vocabulary.TotalFeatures.Should().Be(
                vocabulary.Genres.Count +
                vocabulary.Actors.Count +
                vocabulary.Directors.Count +
                vocabulary.Tags.Count +
                vocabulary.Decades.Count +
                vocabulary.Collections.Count);
        }

        [Fact]
        public void BuildVocabulary_RealTestLibrary_ChristopherNolanHasCorrectIdf()
        {
            // Arrange - Christopher Nolan directs 3 out of 10 movies in test library
            var library = TestMediaLibrary.CreateTestMovies();

            // Act
            var vocabulary = _builder.BuildVocabulary(library);

            // Assert
            var nolanIdf = vocabulary.DirectorIdf["Christopher Nolan"];
            var expectedIdf = (float)Math.Log((double)library.Count / 3.0); // 3 Nolan films
            nolanIdf.Should().BeApproximately(expectedIdf, 0.1f);
        }

        #endregion
    }
}
