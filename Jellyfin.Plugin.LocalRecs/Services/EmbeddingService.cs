using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for computing TF-IDF embeddings for media items.
    /// Converts MediaItemMetadata into numerical vectors for similarity comparison.
    /// </summary>
    public class EmbeddingService
    {
        private readonly ILogger<EmbeddingService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddingService"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public EmbeddingService(ILogger<EmbeddingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Computes embeddings for all items in the collection using the provided vocabulary.
        /// </summary>
        /// <param name="items">The media items.</param>
        /// <param name="vocabulary">The feature vocabulary with IDF values.</param>
        /// <returns>Dictionary mapping item ID to embedding.</returns>
        public IReadOnlyDictionary<Guid, ItemEmbedding> ComputeEmbeddings(
            IEnumerable<MediaItemMetadata> items,
            FeatureVocabulary vocabulary)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (vocabulary == null)
            {
                throw new ArgumentNullException(nameof(vocabulary));
            }

            var itemList = items.ToList();
            _logger.LogInformation("Computing embeddings for {Count} items", itemList.Count);

            var embeddings = new Dictionary<Guid, ItemEmbedding>();

            foreach (var item in itemList)
            {
                var embedding = ComputeEmbedding(item, vocabulary);
                embeddings[item.Id] = embedding;
            }

            if (embeddings.Count > 0)
            {
                _logger.LogInformation(
                    "Computed {Count} embeddings (dimension: {Dim})",
                    embeddings.Count,
                    embeddings.Values.First().Vector.Length);
            }
            else
            {
                _logger.LogInformation("No embeddings computed (0 items provided)");
            }

            return embeddings;
        }

        /// <summary>
        /// Gets the expected embedding dimension for a given vocabulary.
        /// </summary>
        /// <param name="vocabulary">The vocabulary.</param>
        /// <returns>Expected embedding dimension.</returns>
        public int GetEmbeddingDimension(FeatureVocabulary vocabulary)
        {
            if (vocabulary == null)
            {
                throw new ArgumentNullException(nameof(vocabulary));
            }

            // Dimensions: genres + actors + directors + tags + decades + ratings (2)
            return vocabulary.Genres.Count +
                   vocabulary.Actors.Count +
                   vocabulary.Directors.Count +
                   vocabulary.Tags.Count +
                   vocabulary.Decades.Count +
                   2; // community rating + critic rating
        }

        /// <summary>
        /// Computes the embedding for a single media item.
        /// </summary>
        /// <param name="item">The media item.</param>
        /// <param name="vocabulary">The feature vocabulary.</param>
        /// <returns>ItemEmbedding with TF-IDF vector.</returns>
        public ItemEmbedding ComputeEmbedding(MediaItemMetadata item, FeatureVocabulary vocabulary)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (vocabulary == null)
            {
                throw new ArgumentNullException(nameof(vocabulary));
            }

            // Build feature vector components
            var genreVector = ComputeTfIdfVector(item.Genres, vocabulary.GenreIdf);
            var actorVector = ComputeTfIdfVector(item.Actors, vocabulary.ActorIdf);
            var directorVector = ComputeTfIdfVector(item.Directors, vocabulary.DirectorIdf);
            var tagVector = ComputeTfIdfVector(item.Tags, vocabulary.TagIdf);
            var decadeVector = ComputeDecadeVector(item.Decade, vocabulary.DecadeIdf);

            // Compute normalized rating features
            var ratingFeatures = ComputeRatingFeatures(item);

            // Concatenate all features into single vector
            var combinedVector = ConcatenateVectors(
                genreVector,
                actorVector,
                directorVector,
                tagVector,
                decadeVector,
                ratingFeatures);

            // Normalize to unit length for cosine similarity
            var normalizedVector = VectorMath.Normalize(combinedVector);

            return new ItemEmbedding(item.Id, normalizedVector);
        }

        /// <summary>
        /// Computes TF-IDF vector for a list of features.
        /// TF = 1 if feature present in item, 0 otherwise.
        /// Vector has one dimension per vocabulary entry.
        /// </summary>
        /// <param name="features">Features present in the item.</param>
        /// <param name="idfValues">IDF values for each feature in vocabulary.</param>
        /// <returns>TF-IDF vector.</returns>
        private float[] ComputeTfIdfVector(
            IReadOnlyList<string> features,
            IReadOnlyDictionary<string, float> idfValues)
        {
            var vector = new float[idfValues.Count];
            var vocabularyIndex = 0;

            // Create HashSet for O(1) lookups instead of O(n) LINQ.Any
            var featureSet = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);

            foreach (var (vocabFeature, idf) in idfValues)
            {
                // TF = 1 if feature present, 0 otherwise (binary TF)
                var tf = featureSet.Contains(vocabFeature) ? 1.0f : 0.0f;

                // TF-IDF = TF × IDF
                vector[vocabularyIndex] = tf * idf;
                vocabularyIndex++;
            }

            return vector;
        }

        /// <summary>
        /// Computes normalized rating features.
        /// </summary>
        /// <param name="item">The media item.</param>
        /// <returns>2-dimensional vector [community_rating, critic_rating].</returns>
        private float[] ComputeRatingFeatures(MediaItemMetadata item)
        {
            // Normalize ratings to [0, 1] range
            var communityRating = item.CommunityRating.HasValue
                ? Math.Clamp(item.CommunityRating.Value / 10.0f, 0.0f, 1.0f)
                : 0.5f; // Default to middle value if unknown

            var criticRating = item.CriticRating.HasValue
                ? Math.Clamp(item.CriticRating.Value / 100.0f, 0.0f, 1.0f)
                : 0.5f; // Default to middle value if unknown

            return new[] { communityRating, criticRating };
        }

        /// <summary>
        /// Computes TF-IDF vector for a single decade feature.
        /// </summary>
        /// <param name="decade">The decade of the item.</param>
        /// <param name="idfValues">IDF values for each decade in vocabulary.</param>
        /// <returns>TF-IDF vector.</returns>
        private float[] ComputeDecadeVector(string decade, IReadOnlyDictionary<string, float> idfValues)
        {
            var vector = new float[idfValues.Count];
            var index = 0;

            foreach (var (vocabDecade, idf) in idfValues)
            {
                // TF = 1 if decade matches, 0 otherwise (binary TF)
                var tf = string.Equals(decade, vocabDecade, StringComparison.OrdinalIgnoreCase) ? 1.0f : 0.0f;

                // TF-IDF = TF × IDF
                vector[index] = tf * idf;
                index++;
            }

            return vector;
        }

        /// <summary>
        /// Concatenates multiple vectors into a single vector.
        /// </summary>
        /// <param name="vectors">Vectors to concatenate.</param>
        /// <returns>Combined vector.</returns>
        private float[] ConcatenateVectors(params float[][] vectors)
        {
            var totalLength = vectors.Sum(v => v.Length);
            var result = new float[totalLength];
            var offset = 0;

            foreach (var vector in vectors)
            {
                Array.Copy(vector, 0, result, offset, vector.Length);
                offset += vector.Length;
            }

            return result;
        }
    }
}
