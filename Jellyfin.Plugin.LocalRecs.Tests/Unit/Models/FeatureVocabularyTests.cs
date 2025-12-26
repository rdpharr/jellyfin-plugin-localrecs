using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.Models
{
    public class FeatureVocabularyTests
    {
        [Fact]
        public void Constructor_InitializesEmptyCollections()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.Genres.Should().NotBeNull().And.BeEmpty();
            vocabulary.Actors.Should().NotBeNull().And.BeEmpty();
            vocabulary.Directors.Should().NotBeNull().And.BeEmpty();
            vocabulary.Tags.Should().NotBeNull().And.BeEmpty();
            vocabulary.Collections.Should().NotBeNull().And.BeEmpty();
            vocabulary.GenreIdf.Should().NotBeNull().And.BeEmpty();
            vocabulary.ActorIdf.Should().NotBeNull().And.BeEmpty();
            vocabulary.DirectorIdf.Should().NotBeNull().And.BeEmpty();
            vocabulary.TagIdf.Should().NotBeNull().And.BeEmpty();
            vocabulary.TotalItems.Should().Be(0);
            vocabulary.MinReleaseYear.Should().BeNull();
            vocabulary.MaxReleaseYear.Should().BeNull();
        }

        [Fact]
        public void AddGenre_AddsToGenresDictionary()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.AddGenre("Action", 50);
            vocabulary.AddGenre("Sci-Fi", 30);

            vocabulary.Genres.Should().HaveCount(2);
            vocabulary.Genres["Action"].Should().Be(50);
            vocabulary.Genres["Sci-Fi"].Should().Be(30);
        }

        [Fact]
        public void AddActor_AddsToActorsDictionary()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.AddActor("Keanu Reeves", 10);
            vocabulary.AddActor("Tom Hanks", 15);

            vocabulary.Actors.Should().HaveCount(2);
            vocabulary.Actors["Keanu Reeves"].Should().Be(10);
            vocabulary.Actors["Tom Hanks"].Should().Be(15);
        }

        [Fact]
        public void AddDirector_AddsToDirectorsDictionary()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.AddDirector("Christopher Nolan", 8);
            vocabulary.AddDirector("Steven Spielberg", 12);

            vocabulary.Directors.Should().HaveCount(2);
            vocabulary.Directors["Christopher Nolan"].Should().Be(8);
            vocabulary.Directors["Steven Spielberg"].Should().Be(12);
        }

        [Fact]
        public void AddCollection_AddsToCollectionsDictionary()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.AddCollection("Marvel Cinematic Universe", 25);
            vocabulary.AddCollection("Star Wars", 9);

            vocabulary.Collections.Should().HaveCount(2);
            vocabulary.Collections["Marvel Cinematic Universe"].Should().Be(25);
            vocabulary.Collections["Star Wars"].Should().Be(9);
        }

        [Fact]
        public void AddTag_AddsToTagsDictionary()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.AddTag("4K", 100);
            vocabulary.AddTag("HDR", 50);

            vocabulary.Tags.Should().HaveCount(2);
            vocabulary.Tags["4K"].Should().Be(100);
            vocabulary.Tags["HDR"].Should().Be(50);
        }

        [Fact]
        public void SetTagIdf_SetsIdfValue()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.SetTagIdf("4K", 0.8f);
            vocabulary.SetTagIdf("HDR", 1.5f);

            vocabulary.TagIdf.Should().HaveCount(2);
            vocabulary.TagIdf["4K"].Should().Be(0.8f);
            vocabulary.TagIdf["HDR"].Should().Be(1.5f);
        }

        [Fact]
        public void SetGenreIdf_SetsIdfValue()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.SetGenreIdf("Action", 0.5f);
            vocabulary.SetGenreIdf("Drama", 1.2f);

            vocabulary.GenreIdf.Should().HaveCount(2);
            vocabulary.GenreIdf["Action"].Should().Be(0.5f);
            vocabulary.GenreIdf["Drama"].Should().Be(1.2f);
        }

        [Fact]
        public void SetActorIdf_SetsIdfValue()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.SetActorIdf("Actor1", 2.3f);
            vocabulary.SetActorIdf("Actor2", 1.8f);

            vocabulary.ActorIdf.Should().HaveCount(2);
            vocabulary.ActorIdf["Actor1"].Should().Be(2.3f);
            vocabulary.ActorIdf["Actor2"].Should().Be(1.8f);
        }

        [Fact]
        public void SetDirectorIdf_SetsIdfValue()
        {
            var vocabulary = new FeatureVocabulary();

            vocabulary.SetDirectorIdf("Director1", 3.1f);
            vocabulary.SetDirectorIdf("Director2", 2.7f);

            vocabulary.DirectorIdf.Should().HaveCount(2);
            vocabulary.DirectorIdf["Director1"].Should().Be(3.1f);
            vocabulary.DirectorIdf["Director2"].Should().Be(2.7f);
        }

        [Fact]
        public void Dictionaries_ReturnReadOnlyInterfaces()
        {
            var vocabulary = new FeatureVocabulary();
            vocabulary.AddGenre("Test", 1);

            vocabulary.Genres.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, int>>();
            vocabulary.Actors.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, int>>();
            vocabulary.Directors.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, int>>();
            vocabulary.Tags.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, int>>();
            vocabulary.Collections.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, int>>();
            vocabulary.GenreIdf.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, float>>();
            vocabulary.ActorIdf.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, float>>();
            vocabulary.DirectorIdf.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, float>>();
            vocabulary.TagIdf.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<string, float>>();
        }

        [Fact]
        public void TotalFeatures_ReturnsSumOfAllVocabularies()
        {
            var vocabulary = new FeatureVocabulary();
            vocabulary.AddGenre("Genre1", 1);
            vocabulary.AddGenre("Genre2", 2);
            vocabulary.AddActor("Actor1", 1);
            vocabulary.AddDirector("Director1", 1);
            vocabulary.AddTag("Tag1", 1);
            vocabulary.AddTag("Tag2", 1);
            vocabulary.AddCollection("Collection1", 1);

            vocabulary.TotalFeatures.Should().Be(7); // 2 genres + 1 actor + 1 director + 2 tags + 1 collection
        }

        [Fact]
        public void TotalItems_CanBeSet()
        {
            var vocabulary = new FeatureVocabulary
            {
                TotalItems = 2000,
            };

            vocabulary.TotalItems.Should().Be(2000);
        }

        [Fact]
        public void ReleaseYearRange_CanBeSet()
        {
            var vocabulary = new FeatureVocabulary
            {
                MinReleaseYear = 1970,
                MaxReleaseYear = 2024,
            };

            vocabulary.MinReleaseYear.Should().Be(1970);
            vocabulary.MaxReleaseYear.Should().Be(2024);
        }

        [Fact]
        public void ComputedAt_DefaultsToRecentTime()
        {
            var before = DateTime.UtcNow;
            var vocabulary = new FeatureVocabulary();
            var after = DateTime.UtcNow;

            vocabulary.ComputedAt.Should().BeOnOrAfter(before);
            vocabulary.ComputedAt.Should().BeOnOrBefore(after);
        }

        [Fact]
        public void Add_UpdatesExistingValue()
        {
            var vocabulary = new FeatureVocabulary();
            vocabulary.AddGenre("Action", 10);
            vocabulary.AddGenre("Action", 20); // Update

            vocabulary.Genres["Action"].Should().Be(20);
            vocabulary.Genres.Should().HaveCount(1);
        }
    }
}
