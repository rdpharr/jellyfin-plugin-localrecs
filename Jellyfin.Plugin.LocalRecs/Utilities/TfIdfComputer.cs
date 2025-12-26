using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Utilities
{
    /// <summary>
    /// Pure functions for TF-IDF computation.
    /// </summary>
    public static class TfIdfComputer
    {
        /// <summary>
        /// Computes the Inverse Document Frequency (IDF) for a feature.
        /// IDF = log(total_documents / documents_with_feature).
        /// </summary>
        /// <param name="totalDocuments">Total number of documents.</param>
        /// <param name="documentsWithFeature">Number of documents containing the feature.</param>
        /// <returns>IDF value.</returns>
        /// <exception cref="ArgumentException">Thrown when totalDocuments is less than 1 or documentsWithFeature is less than 1 or greater than totalDocuments.</exception>
        public static float ComputeIdf(int totalDocuments, int documentsWithFeature)
        {
            if (totalDocuments < 1)
            {
                throw new ArgumentException("Total documents must be at least 1", nameof(totalDocuments));
            }

            if (documentsWithFeature < 1)
            {
                throw new ArgumentException("Documents with feature must be at least 1", nameof(documentsWithFeature));
            }

            if (documentsWithFeature > totalDocuments)
            {
                throw new ArgumentException(
                    $"Documents with feature ({documentsWithFeature}) cannot exceed total documents ({totalDocuments})");
            }

            return (float)Math.Log((double)totalDocuments / documentsWithFeature);
        }

        /// <summary>
        /// Computes TF-IDF values for a set of features in a document.
        /// TF = 1 if feature present, 0 otherwise (binary TF).
        /// TF-IDF = TF × IDF.
        /// </summary>
        /// <param name="documentFeatures">Features present in the document.</param>
        /// <param name="idfValues">IDF values for each feature.</param>
        /// <returns>Dictionary of feature → TF-IDF value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public static Dictionary<string, float> ComputeTfIdf(
            IEnumerable<string> documentFeatures,
            IDictionary<string, float> idfValues)
        {
            if (documentFeatures == null)
            {
                throw new ArgumentNullException(nameof(documentFeatures));
            }

            if (idfValues == null)
            {
                throw new ArgumentNullException(nameof(idfValues));
            }

            var result = new Dictionary<string, float>();

            foreach (var feature in documentFeatures)
            {
                if (idfValues.TryGetValue(feature, out float idf))
                {
                    // Binary TF: 1 if present, 0 otherwise
                    result[feature] = 1.0f * idf;
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a TF-IDF vector from feature scores, maintaining vocabulary order.
        /// </summary>
        /// <param name="tfIdfScores">TF-IDF scores for features present in the document.</param>
        /// <param name="vocabularyIndex">Mapping of feature name → vector index.</param>
        /// <param name="vectorSize">Total size of the output vector.</param>
        /// <returns>Dense TF-IDF vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when vectorSize is less than 1.</exception>
        public static float[] BuildVector(
            IDictionary<string, float> tfIdfScores,
            IDictionary<string, int> vocabularyIndex,
            int vectorSize)
        {
            if (tfIdfScores == null)
            {
                throw new ArgumentNullException(nameof(tfIdfScores));
            }

            if (vocabularyIndex == null)
            {
                throw new ArgumentNullException(nameof(vocabularyIndex));
            }

            if (vectorSize < 1)
            {
                throw new ArgumentException("Vector size must be at least 1", nameof(vectorSize));
            }

            var vector = new float[vectorSize];

            foreach (var kvp in tfIdfScores)
            {
                if (vocabularyIndex.TryGetValue(kvp.Key, out int index))
                {
                    if (index < 0 || index >= vectorSize)
                    {
                        throw new ArgumentException(
                            $"Vocabulary index for '{kvp.Key}' ({index}) is out of bounds for vector size {vectorSize}");
                    }

                    vector[index] = kvp.Value;
                }
            }

            return vector;
        }

        /// <summary>
        /// Normalizes a scalar value to the range [0, 1].
        /// </summary>
        /// <param name="value">The value to normalize.</param>
        /// <param name="min">Minimum value in the range.</param>
        /// <param name="max">Maximum value in the range.</param>
        /// <returns>Normalized value.</returns>
        /// <exception cref="ArgumentException">Thrown when max is less than or equal to min.</exception>
        public static float NormalizeScalar(float value, float min, float max)
        {
            if (max <= min)
            {
                throw new ArgumentException($"Max ({max}) must be greater than min ({min})");
            }

            if (value <= min)
            {
                return 0;
            }

            if (value >= max)
            {
                return 1;
            }

            return (value - min) / (max - min);
        }

        /// <summary>
        /// Creates a one-hot encoding for a categorical value.
        /// </summary>
        /// <param name="value">The categorical value.</param>
        /// <param name="vocabularyIndex">Mapping of value → index.</param>
        /// <param name="vocabularySize">Total vocabulary size.</param>
        /// <returns>One-hot encoded vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vocabularyIndex is null.</exception>
        /// <exception cref="ArgumentException">Thrown when vocabularySize is less than 1.</exception>
        public static float[] OneHotEncode(
            string? value,
            IDictionary<string, int> vocabularyIndex,
            int vocabularySize)
        {
            if (vocabularyIndex == null)
            {
                throw new ArgumentNullException(nameof(vocabularyIndex));
            }

            if (vocabularySize < 1)
            {
                throw new ArgumentException("Vocabulary size must be at least 1", nameof(vocabularySize));
            }

            var vector = new float[vocabularySize];

            if (value != null && vocabularyIndex.TryGetValue(value, out int index))
            {
                if (index >= 0 && index < vocabularySize)
                {
                    vector[index] = 1.0f;
                }
            }

            return vector;
        }
    }
}
