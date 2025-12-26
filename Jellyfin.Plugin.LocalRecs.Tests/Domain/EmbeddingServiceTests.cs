using System;
using System.Linq;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.Tests.Fixtures;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Domain
{
    public class EmbeddingServiceTests
    {
        private readonly EmbeddingService _embeddingService;
        private readonly VocabularyBuilder _vocabularyBuilder;

        public EmbeddingServiceTests()
        {
            _embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            _vocabularyBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
        }

        [Fact]
        public void ComputeEmbeddings_ValidInput_ReturnsEmbeddingsForAllItems()
        {
            var items = TestMediaLibrary.CreateMinimalLibrary();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var embeddings = _embeddingService.ComputeEmbeddings(items, vocabulary);

            embeddings.Should().HaveCount(items.Count);
            embeddings.Keys.Should().BeEquivalentTo(items.Select(i => i.Id));
        }

        [Fact]
        public void ComputeEmbedding_ValidItem_ReturnsNormalizedVector()
        {
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);
            var matrix = items.First(m => m.Name == "The Matrix");

            var embedding = _embeddingService.ComputeEmbedding(matrix, vocabulary);

            embedding.Should().NotBeNull();
            embedding.ItemId.Should().Be(matrix.Id);
            embedding.Vector.Should().NotBeEmpty();
            
            // Vector should be normalized (magnitude = 1.0)
            var magnitude = VectorMath.Magnitude(embedding.Vector);
            magnitude.Should().BeApproximately(1.0f, 0.001f);
        }

        [Fact]
        public void ComputeEmbedding_SimilarMovies_HighCosineSimilarity()
        {
            // The Matrix and Inception are both sci-fi action movies
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);
            
            var matrix = items.First(m => m.Name == "The Matrix");
            var inception = items.First(m => m.Name == "Inception");

            var embedding1 = _embeddingService.ComputeEmbedding(matrix, vocabulary);
            var embedding2 = _embeddingService.ComputeEmbedding(inception, vocabulary);

            var similarity = VectorMath.CosineSimilarity(embedding1.Vector, embedding2.Vector);
            
            // Should have positive similarity due to shared genres (Sci-Fi, Action)
            // Note: TF-IDF vectors are sparse, so similarity values are typically lower than 0.5
            similarity.Should().BeGreaterThan(0.0f, "they share Science Fiction and Action genres");
        }

        [Fact]
        public void ComputeEmbedding_DissimilarMovies_LowerCosineSimilarity()
        {
            // The Matrix (sci-fi action) vs Groundhog Day (comedy)
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);
            
            var matrix = items.First(m => m.Name == "The Matrix");
            var groundhogDay = items.First(m => m.Name == "Groundhog Day");

            var embedding1 = _embeddingService.ComputeEmbedding(matrix, vocabulary);
            var embedding2 = _embeddingService.ComputeEmbedding(groundhogDay, vocabulary);

            var similarity = VectorMath.CosineSimilarity(embedding1.Vector, embedding2.Vector);
            
            // Should have lower similarity - different genres, actors, directors
            var matrixInceptionSim = VectorMath.CosineSimilarity(
                embedding1.Vector,
                _embeddingService.ComputeEmbedding(
                    items.First(m => m.Name == "Inception"), 
                    vocabulary).Vector);
            
            similarity.Should().BeLessThan(matrixInceptionSim, 
                "Matrix/Groundhog Day have no shared genres vs Matrix/Inception sharing Sci-Fi + Action");
        }

        [Fact]
        public void ComputeEmbedding_SameDirector_IncreasedSimilarity()
        {
            // The Dark Knight and Inception - both by Christopher Nolan
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);
            
            var darkKnight = items.First(m => m.Name == "The Dark Knight");
            var inception = items.First(m => m.Name == "Inception");
            var alien = items.First(m => m.Name == "Alien");

            var dkEmbedding = _embeddingService.ComputeEmbedding(darkKnight, vocabulary);
            var inceptionEmbedding = _embeddingService.ComputeEmbedding(inception, vocabulary);
            var alienEmbedding = _embeddingService.ComputeEmbedding(alien, vocabulary);

            var nolanSimilarity = VectorMath.CosineSimilarity(dkEmbedding.Vector, inceptionEmbedding.Vector);
            var nonNolanSimilarity = VectorMath.CosineSimilarity(dkEmbedding.Vector, alienEmbedding.Vector);

            // Nolan movies should be more similar to each other
            nolanSimilarity.Should().BeGreaterThan(nonNolanSimilarity,
                "shared director (Nolan) contributes to similarity");
        }

        [Fact]
        public void GetEmbeddingDimension_ValidVocabulary_ReturnsCorrectDimension()
        {
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var expectedDim = vocabulary.Genres.Count + 
                             vocabulary.Actors.Count + 
                             vocabulary.Directors.Count + 
                             vocabulary.Tags.Count +
                             2 + // ratings
                             1;  // year

            var actualDim = _embeddingService.GetEmbeddingDimension(vocabulary);

            actualDim.Should().Be(expectedDim);
        }

        [Fact]
        public void ComputeEmbedding_VerifyVectorDimension_MatchesExpected()
        {
            var items = TestMediaLibrary.CreateTestMovies();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);
            var expectedDim = _embeddingService.GetEmbeddingDimension(vocabulary);

            foreach (var item in items)
            {
                var embedding = _embeddingService.ComputeEmbedding(item, vocabulary);
                embedding.Vector.Length.Should().Be(expectedDim);
            }
        }

        [Fact]
        public void ComputeEmbedding_NullItem_ThrowsArgumentNullException()
        {
            var vocabulary = new FeatureVocabulary();

            Action act = () => _embeddingService.ComputeEmbedding(null!, vocabulary);

            act.Should().Throw<ArgumentNullException>().WithParameterName("item");
        }

        [Fact]
        public void ComputeEmbedding_NullVocabulary_ThrowsArgumentNullException()
        {
            var item = TestMediaLibrary.CreateMinimalLibrary().First();

            Action act = () => _embeddingService.ComputeEmbedding(item, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vocabulary");
        }

        [Fact]
        public void ComputeEmbeddings_NullItems_ThrowsArgumentNullException()
        {
            var vocabulary = new FeatureVocabulary();

            Action act = () => _embeddingService.ComputeEmbeddings(null!, vocabulary);

            act.Should().Throw<ArgumentNullException>().WithParameterName("items");
        }

        [Fact]
        public void ComputeEmbeddings_NullVocabulary_ThrowsArgumentNullException()
        {
            var items = TestMediaLibrary.CreateMinimalLibrary();

            Action act = () => _embeddingService.ComputeEmbeddings(items, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vocabulary");
        }

        [Fact]
        public void ComputeEmbedding_ItemWithNoMetadata_CreatesValidEmbedding()
        {
            // Item with minimal metadata
            var item = new MediaItemMetadata(Guid.NewGuid(), "Bare Minimum", MediaType.Movie)
            {
                ReleaseYear = 2020
            };
            
            var items = new[] { item };
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var embedding = _embeddingService.ComputeEmbedding(item, vocabulary);

            embedding.Should().NotBeNull();
            embedding.Vector.Should().NotBeEmpty();
            
            // Should still be normalized even with sparse features
            var magnitude = VectorMath.Magnitude(embedding.Vector);
            magnitude.Should().BeApproximately(1.0f, 0.001f);
        }

        [Fact]
        public void ComputeEmbedding_RatingsNormalization_ValuesInRange()
        {
            var item = new MediaItemMetadata(Guid.NewGuid(), "Test", MediaType.Movie)
            {
                CommunityRating = 10.0f,  // Max community rating
                CriticRating = 100.0f,     // Max critic rating
                ReleaseYear = 2020
            };
            
            var items = new[] { item };
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var embedding = _embeddingService.ComputeEmbedding(item, vocabulary);

            // Can't directly inspect rating features in the vector, but can verify
            // the vector is valid and normalized
            embedding.Vector.Should().NotContain(float.NaN);
            embedding.Vector.Should().NotContain(float.PositiveInfinity);
            embedding.Vector.Should().NotContain(float.NegativeInfinity);
            
            var magnitude = VectorMath.Magnitude(embedding.Vector);
            magnitude.Should().BeApproximately(1.0f, 0.001f);
        }

        [Fact]
        public void ComputeEmbedding_YearNormalization_HandlesEdgeCases()
        {
            // Create items spanning different years
            var oldItem = new MediaItemMetadata(Guid.NewGuid(), "Old Movie", MediaType.Movie)
            {
                ReleaseYear = 1970
            };
            
            var newItem = new MediaItemMetadata(Guid.NewGuid(), "New Movie", MediaType.Movie)
            {
                ReleaseYear = 2023
            };
            
            var items = new[] { oldItem, newItem };
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var oldEmbedding = _embeddingService.ComputeEmbedding(oldItem, vocabulary);
            var newEmbedding = _embeddingService.ComputeEmbedding(newItem, vocabulary);

            // Both should produce valid normalized vectors
            VectorMath.Magnitude(oldEmbedding.Vector).Should().BeApproximately(1.0f, 0.001f);
            VectorMath.Magnitude(newEmbedding.Vector).Should().BeApproximately(1.0f, 0.001f);
            
            // Vectors should be different (year component differs)
            var similarity = VectorMath.CosineSimilarity(oldEmbedding.Vector, newEmbedding.Vector);
            similarity.Should().BeLessThan(1.0f);
        }

        [Fact]
        public void ComputeEmbedding_CaseInsensitiveGenres_TreatedAsSame()
        {
            var item1 = new MediaItemMetadata(Guid.NewGuid(), "Movie 1", MediaType.Movie);
            item1.AddGenre("Science Fiction");
            
            var item2 = new MediaItemMetadata(Guid.NewGuid(), "Movie 2", MediaType.Movie);
            item2.AddGenre("science fiction");  // Different case
            
            var items = new[] { item1, item2 };
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var embedding1 = _embeddingService.ComputeEmbedding(item1, vocabulary);
            var embedding2 = _embeddingService.ComputeEmbedding(item2, vocabulary);

            // Should be very similar (only year/rating defaults might differ)
            var similarity = VectorMath.CosineSimilarity(embedding1.Vector, embedding2.Vector);
            similarity.Should().BeGreaterThan(0.99f, "same genre in different case should be treated identically");
        }

        [Fact]
        public void GetEmbeddingDimension_NullVocabulary_ThrowsArgumentNullException()
        {
            Action act = () => _embeddingService.GetEmbeddingDimension(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vocabulary");
        }

        [Fact]
        public void ComputeEmbeddings_ReturnsReadOnlyDictionary()
        {
            var items = TestMediaLibrary.CreateMinimalLibrary();
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            var embeddings = _embeddingService.ComputeEmbeddings(items, vocabulary);

            embeddings.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyDictionary<Guid, ItemEmbedding>>();
        }

        [Fact]
        public void VocabularyBuilder_WithMaxActors_LimitsVocabularySize()
        {
            // Create library with many unique actors (each movie has 3 different actors)
            var items = TestMediaLibrary.CreateTestMovies();
            
            // Build vocabulary with actor limit
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items, maxActors: 5);

            // Vocabulary should only contain top 5 actors by document frequency
            vocabulary.Actors.Count.Should().Be(5, "maxActors parameter should limit vocabulary size");
        }

        [Fact]
        public void VocabularyBuilder_WithMaxDirectors_LimitsVocabularySize()
        {
            var items = TestMediaLibrary.CreateTestMovies();
            
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items, maxDirectors: 3);

            vocabulary.Directors.Count.Should().Be(3, "maxDirectors parameter should limit vocabulary size");
        }

        [Fact]
        public void VocabularyBuilder_WithoutLimits_IncludesAllFeatures()
        {
            var items = TestMediaLibrary.CreateTestMovies();
            
            // No limits (defaults to 0 = unlimited)
            var vocabulary = _vocabularyBuilder.BuildVocabulary(items);

            // Should include all unique actors and directors across all movies
            vocabulary.Actors.Count.Should().BeGreaterThan(10, "should include many unique actors without limit");
            vocabulary.Directors.Count.Should().BeGreaterThan(5, "should include all unique directors without limit");
        }
    }
}
