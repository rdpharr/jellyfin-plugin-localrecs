using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.Models
{
    public class MediaItemMetadataTests
    {
        [Fact]
        public void Constructor_ValidInputs_CreatesInstance()
        {
            var id = Guid.NewGuid();
            var name = "The Matrix";
            var type = MediaType.Movie;

            var item = new MediaItemMetadata(id, name, type);

            item.Id.Should().Be(id);
            item.Name.Should().Be(name);
            item.Type.Should().Be(type);
            item.Genres.Should().NotBeNull().And.BeEmpty();
            item.Actors.Should().NotBeNull().And.BeEmpty();
            item.Directors.Should().NotBeNull().And.BeEmpty();
            item.Tags.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Constructor_NullName_ThrowsArgumentNullException()
        {
            var id = Guid.NewGuid();
            var type = MediaType.Movie;

            Action act = () => new MediaItemMetadata(id, null!, type);

            act.Should().Throw<ArgumentNullException>().WithParameterName("name");
        }

        [Fact]
        public void AddGenre_AddsGenreToCollection()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);

            item.AddGenre("Action");
            item.AddGenre("Sci-Fi");

            item.Genres.Should().HaveCount(2);
            item.Genres.Should().Contain("Action");
            item.Genres.Should().Contain("Sci-Fi");
        }

        [Fact]
        public void AddActor_AddsActorToCollection()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);

            item.AddActor("Keanu Reeves");
            item.AddActor("Laurence Fishburne");

            item.Actors.Should().HaveCount(2);
            item.Actors.Should().Contain("Keanu Reeves");
            item.Actors.Should().Contain("Laurence Fishburne");
        }

        [Fact]
        public void AddDirector_AddsDirectorToCollection()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);

            item.AddDirector("Lana Wachowski");
            item.AddDirector("Lilly Wachowski");

            item.Directors.Should().HaveCount(2);
            item.Directors.Should().Contain("Lana Wachowski");
            item.Directors.Should().Contain("Lilly Wachowski");
        }

        [Fact]
        public void Genres_ReturnsReadOnlyList()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);
            item.AddGenre("Action");

            var genres = item.Genres;

            genres.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyList<string>>();
            genres.Should().HaveCount(1);
        }

        [Fact]
        public void Actors_ReturnsReadOnlyList()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);
            item.AddActor("Actor 1");

            var actors = item.Actors;

            actors.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyList<string>>();
            actors.Should().HaveCount(1);
        }

        [Fact]
        public void Directors_ReturnsReadOnlyList()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);
            item.AddDirector("Director 1");

            var directors = item.Directors;

            directors.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyList<string>>();
            directors.Should().HaveCount(1);
        }

        [Fact]
        public void AddTag_AddsTagToCollection()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);

            item.AddTag("4K");
            item.AddTag("HDR");

            item.Tags.Should().HaveCount(2);
            item.Tags.Should().Contain("4K");
            item.Tags.Should().Contain("HDR");
        }

        [Fact]
        public void Tags_ReturnsReadOnlyList()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);
            item.AddTag("4K");

            var tags = item.Tags;

            tags.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyList<string>>();
            tags.Should().HaveCount(1);
        }

        [Fact]
        public void OptionalProperties_DefaultToNull()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie);

            item.CommunityRating.Should().BeNull();
            item.CriticRating.Should().BeNull();
            item.ReleaseYear.Should().BeNull();
            item.CollectionName.Should().BeNull();
            item.TmdbId.Should().BeNull();
            item.TvdbId.Should().BeNull();
            item.Path.Should().BeNull();
        }

        [Fact]
        public void OptionalProperties_CanBeSet()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test Movie", MediaType.Movie)
            {
                CommunityRating = 8.7f,
                CriticRating = 87.0f,
                ReleaseYear = 1999,
                CollectionName = "The Matrix Collection",
                TmdbId = "603",
                Path = "/media/movies/matrix.mkv",
            };

            item.CommunityRating.Should().Be(8.7f);
            item.CriticRating.Should().Be(87.0f);
            item.ReleaseYear.Should().Be(1999);
            item.CollectionName.Should().Be("The Matrix Collection");
            item.TmdbId.Should().Be("603");
            item.Path.Should().Be("/media/movies/matrix.mkv");
        }

        [Fact]
        public void Type_Movie_IsCorrect()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Movie", MediaType.Movie);

            item.Type.Should().Be(MediaType.Movie);
        }

        [Fact]
        public void Type_Series_IsCorrect()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Series", MediaType.Series);

            item.Type.Should().Be(MediaType.Series);
        }
    }
}
