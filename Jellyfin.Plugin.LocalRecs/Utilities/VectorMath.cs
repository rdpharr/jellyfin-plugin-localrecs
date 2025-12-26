using System;

namespace Jellyfin.Plugin.LocalRecs.Utilities
{
    /// <summary>
    /// Pure functions for vector operations (cosine similarity, normalization, weighted sum).
    /// </summary>
    public static class VectorMath
    {
        /// <summary>
        /// Computes the cosine similarity between two vectors.
        /// </summary>
        /// <param name="vectorA">First vector.</param>
        /// <param name="vectorB">Second vector.</param>
        /// <returns>Cosine similarity (range: -1 to 1, where 1 = identical, 0 = orthogonal, -1 = opposite).</returns>
        /// <exception cref="ArgumentNullException">Thrown when either vector is null.</exception>
        /// <exception cref="ArgumentException">Thrown when vectors have different lengths or are empty.</exception>
        public static float CosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA == null)
            {
                throw new ArgumentNullException(nameof(vectorA));
            }

            if (vectorB == null)
            {
                throw new ArgumentNullException(nameof(vectorB));
            }

            if (vectorA.Length == 0)
            {
                throw new ArgumentException("Vector cannot be empty", nameof(vectorA));
            }

            if (vectorA.Length != vectorB.Length)
            {
                throw new ArgumentException(
                    $"Vectors must have the same length. A: {vectorA.Length}, B: {vectorB.Length}");
            }

            float dotProduct = DotProduct(vectorA, vectorB);
            float magnitudeA = Magnitude(vectorA);
            float magnitudeB = Magnitude(vectorB);

            if (magnitudeA == 0 || magnitudeB == 0)
            {
                return 0; // Avoid division by zero; zero vector has no direction
            }

            return dotProduct / (magnitudeA * magnitudeB);
        }

        /// <summary>
        /// Computes the dot product of two vectors.
        /// </summary>
        /// <param name="vectorA">First vector.</param>
        /// <param name="vectorB">Second vector.</param>
        /// <returns>Dot product.</returns>
        public static float DotProduct(float[] vectorA, float[] vectorB)
        {
            float sum = 0;
            for (int i = 0; i < vectorA.Length; i++)
            {
                sum += vectorA[i] * vectorB[i];
            }

            return sum;
        }

        /// <summary>
        /// Computes the magnitude (L2 norm) of a vector.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <returns>Magnitude.</returns>
        public static float Magnitude(float[] vector)
        {
            float sumOfSquares = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                sumOfSquares += vector[i] * vector[i];
            }

            return (float)Math.Sqrt(sumOfSquares);
        }

        /// <summary>
        /// Normalizes a vector to unit length (L2 normalization).
        /// </summary>
        /// <param name="vector">The vector to normalize.</param>
        /// <returns>Normalized vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vector is null.</exception>
        public static float[] Normalize(float[] vector)
        {
            if (vector == null)
            {
                throw new ArgumentNullException(nameof(vector));
            }

            float magnitude = Magnitude(vector);

            if (magnitude == 0)
            {
                return new float[vector.Length]; // Zero vector cannot be normalized
            }

            var normalized = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                normalized[i] = vector[i] / magnitude;
            }

            return normalized;
        }

        /// <summary>
        /// Computes the weighted sum of vectors.
        /// </summary>
        /// <param name="vectors">Array of vectors.</param>
        /// <param name="weights">Corresponding weights.</param>
        /// <returns>Weighted sum vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vectors or weights are null.</exception>
        /// <exception cref="ArgumentException">Thrown when vectors and weights have different lengths or are empty.</exception>
        public static float[] WeightedSum(float[][] vectors, float[] weights)
        {
            if (vectors == null)
            {
                throw new ArgumentNullException(nameof(vectors));
            }

            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (vectors.Length == 0)
            {
                throw new ArgumentException("Vectors array cannot be empty", nameof(vectors));
            }

            if (vectors.Length != weights.Length)
            {
                throw new ArgumentException(
                    $"Number of vectors and weights must match. Vectors: {vectors.Length}, Weights: {weights.Length}");
            }

            int dimensions = vectors[0].Length;
            var result = new float[dimensions];

            for (int i = 0; i < vectors.Length; i++)
            {
                if (vectors[i].Length != dimensions)
                {
                    throw new ArgumentException(
                        $"All vectors must have the same length. Expected: {dimensions}, Got: {vectors[i].Length} at index {i}");
                }

                for (int j = 0; j < dimensions; j++)
                {
                    result[j] += vectors[i][j] * weights[i];
                }
            }

            return result;
        }

        /// <summary>
        /// Adds two vectors element-wise.
        /// </summary>
        /// <param name="vectorA">First vector.</param>
        /// <param name="vectorB">Second vector.</param>
        /// <returns>Sum vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either vector is null.</exception>
        /// <exception cref="ArgumentException">Thrown when vectors have different lengths.</exception>
        public static float[] Add(float[] vectorA, float[] vectorB)
        {
            if (vectorA == null)
            {
                throw new ArgumentNullException(nameof(vectorA));
            }

            if (vectorB == null)
            {
                throw new ArgumentNullException(nameof(vectorB));
            }

            if (vectorA.Length != vectorB.Length)
            {
                throw new ArgumentException(
                    $"Vectors must have the same length. A: {vectorA.Length}, B: {vectorB.Length}");
            }

            var result = new float[vectorA.Length];
            for (int i = 0; i < vectorA.Length; i++)
            {
                result[i] = vectorA[i] + vectorB[i];
            }

            return result;
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <param name="scalar">The scalar value.</param>
        /// <returns>Scaled vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vector is null.</exception>
        public static float[] Scale(float[] vector, float scalar)
        {
            if (vector == null)
            {
                throw new ArgumentNullException(nameof(vector));
            }

            var result = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                result[i] = vector[i] * scalar;
            }

            return result;
        }
    }
}
