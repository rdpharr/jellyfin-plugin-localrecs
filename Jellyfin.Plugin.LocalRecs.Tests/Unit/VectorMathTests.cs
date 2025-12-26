using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    public class VectorMathTests
    {
        [Fact]
        public void CosineSimilarity_IdenticalVectors_ReturnsOne()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { 1, 2, 3 };

            var result = VectorMath.CosineSimilarity(vectorA, vectorB);

            result.Should().BeApproximately(1.0f, 0.0001f);
        }

        [Fact]
        public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
        {
            var vectorA = new float[] { 1, 0, 0 };
            var vectorB = new float[] { 0, 1, 0 };

            var result = VectorMath.CosineSimilarity(vectorA, vectorB);

            result.Should().BeApproximately(0.0f, 0.0001f);
        }

        [Fact]
        public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { -1, -2, -3 };

            var result = VectorMath.CosineSimilarity(vectorA, vectorB);

            result.Should().BeApproximately(-1.0f, 0.0001f);
        }

        [Fact]
        public void CosineSimilarity_NullVectorA_ThrowsArgumentNullException()
        {
            var vectorB = new float[] { 1, 2, 3 };

            Action act = () => VectorMath.CosineSimilarity(null!, vectorB);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vectorA");
        }

        [Fact]
        public void CosineSimilarity_NullVectorB_ThrowsArgumentNullException()
        {
            var vectorA = new float[] { 1, 2, 3 };

            Action act = () => VectorMath.CosineSimilarity(vectorA, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vectorB");
        }

        [Fact]
        public void CosineSimilarity_EmptyVector_ThrowsArgumentException()
        {
            var vectorA = Array.Empty<float>();
            var vectorB = new float[] { 1, 2, 3 };

            Action act = () => VectorMath.CosineSimilarity(vectorA, vectorB);

            act.Should().Throw<ArgumentException>().WithParameterName("vectorA");
        }

        [Fact]
        public void CosineSimilarity_DifferentLengths_ThrowsArgumentException()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { 1, 2 };

            Action act = () => VectorMath.CosineSimilarity(vectorA, vectorB);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CosineSimilarity_ZeroVector_ReturnsZero()
        {
            var vectorA = new float[] { 0, 0, 0 };
            var vectorB = new float[] { 1, 2, 3 };

            var result = VectorMath.CosineSimilarity(vectorA, vectorB);

            result.Should().Be(0.0f);
        }

        [Fact]
        public void DotProduct_ReturnsCorrectValue()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { 4, 5, 6 };

            var result = VectorMath.DotProduct(vectorA, vectorB);

            result.Should().Be(32.0f); // 1*4 + 2*5 + 3*6 = 32
        }

        [Fact]
        public void Magnitude_ReturnsCorrectValue()
        {
            var vector = new float[] { 3, 4 };

            var result = VectorMath.Magnitude(vector);

            result.Should().BeApproximately(5.0f, 0.0001f); // sqrt(3^2 + 4^2) = 5
        }

        [Fact]
        public void Magnitude_ZeroVector_ReturnsZero()
        {
            var vector = new float[] { 0, 0, 0 };

            var result = VectorMath.Magnitude(vector);

            result.Should().Be(0.0f);
        }

        [Fact]
        public void Normalize_ReturnsUnitVector()
        {
            var vector = new float[] { 3, 4 };

            var result = VectorMath.Normalize(vector);

            result.Should().HaveCount(2);
            result[0].Should().BeApproximately(0.6f, 0.0001f); // 3/5
            result[1].Should().BeApproximately(0.8f, 0.0001f); // 4/5
            VectorMath.Magnitude(result).Should().BeApproximately(1.0f, 0.0001f);
        }

        [Fact]
        public void Normalize_ZeroVector_ReturnsZeroVector()
        {
            var vector = new float[] { 0, 0, 0 };

            var result = VectorMath.Normalize(vector);

            result.Should().Equal(0, 0, 0);
        }

        [Fact]
        public void Normalize_NullVector_ThrowsArgumentNullException()
        {
            Action act = () => VectorMath.Normalize(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vector");
        }

        [Fact]
        public void WeightedSum_ReturnsCorrectValue()
        {
            var vectors = new float[][]
            {
                new float[] { 1, 2, 3 },
                new float[] { 4, 5, 6 },
            };
            var weights = new float[] { 0.5f, 0.5f };

            var result = VectorMath.WeightedSum(vectors, weights);

            result.Should().HaveCount(3);
            result[0].Should().BeApproximately(2.5f, 0.0001f); // (1*0.5 + 4*0.5)
            result[1].Should().BeApproximately(3.5f, 0.0001f); // (2*0.5 + 5*0.5)
            result[2].Should().BeApproximately(4.5f, 0.0001f); // (3*0.5 + 6*0.5)
        }

        [Fact]
        public void WeightedSum_NullVectors_ThrowsArgumentNullException()
        {
            var weights = new float[] { 0.5f, 0.5f };

            Action act = () => VectorMath.WeightedSum(null!, weights);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vectors");
        }

        [Fact]
        public void WeightedSum_NullWeights_ThrowsArgumentNullException()
        {
            var vectors = new float[][]
            {
                new float[] { 1, 2, 3 },
            };

            Action act = () => VectorMath.WeightedSum(vectors, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("weights");
        }

        [Fact]
        public void WeightedSum_EmptyVectors_ThrowsArgumentException()
        {
            var vectors = Array.Empty<float[]>();
            var weights = Array.Empty<float>();

            Action act = () => VectorMath.WeightedSum(vectors, weights);

            act.Should().Throw<ArgumentException>().WithParameterName("vectors");
        }

        [Fact]
        public void WeightedSum_MismatchedLengths_ThrowsArgumentException()
        {
            var vectors = new float[][]
            {
                new float[] { 1, 2, 3 },
                new float[] { 4, 5, 6 },
            };
            var weights = new float[] { 0.5f };

            Action act = () => VectorMath.WeightedSum(vectors, weights);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void WeightedSum_DifferentVectorDimensions_ThrowsArgumentException()
        {
            var vectors = new float[][]
            {
                new float[] { 1, 2, 3 },
                new float[] { 4, 5 },
            };
            var weights = new float[] { 0.5f, 0.5f };

            Action act = () => VectorMath.WeightedSum(vectors, weights);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Add_ReturnsCorrectSum()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { 4, 5, 6 };

            var result = VectorMath.Add(vectorA, vectorB);

            result.Should().Equal(5, 7, 9);
        }

        [Fact]
        public void Add_NullVectorA_ThrowsArgumentNullException()
        {
            var vectorB = new float[] { 1, 2, 3 };

            Action act = () => VectorMath.Add(null!, vectorB);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vectorA");
        }

        [Fact]
        public void Add_NullVectorB_ThrowsArgumentNullException()
        {
            var vectorA = new float[] { 1, 2, 3 };

            Action act = () => VectorMath.Add(vectorA, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vectorB");
        }

        [Fact]
        public void Add_DifferentLengths_ThrowsArgumentException()
        {
            var vectorA = new float[] { 1, 2, 3 };
            var vectorB = new float[] { 1, 2 };

            Action act = () => VectorMath.Add(vectorA, vectorB);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Scale_ReturnsCorrectScaledVector()
        {
            var vector = new float[] { 1, 2, 3 };

            var result = VectorMath.Scale(vector, 2.0f);

            result.Should().Equal(2, 4, 6);
        }

        [Fact]
        public void Scale_NullVector_ThrowsArgumentNullException()
        {
            Action act = () => VectorMath.Scale(null!, 2.0f);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vector");
        }

        [Fact]
        public void Scale_ZeroScalar_ReturnsZeroVector()
        {
            var vector = new float[] { 1, 2, 3 };

            var result = VectorMath.Scale(vector, 0.0f);

            result.Should().Equal(0, 0, 0);
        }

        [Fact]
        public void Scale_NegativeScalar_ReturnsNegativeVector()
        {
            var vector = new float[] { 1, 2, 3 };

            var result = VectorMath.Scale(vector, -1.0f);

            result.Should().Equal(-1, -2, -3);
        }

        [Fact]
        public void WeightedSum_SingleVector_ReturnsScaledVector()
        {
            var vectors = new float[][] { new float[] { 1, 2, 3 } };
            var weights = new float[] { 2.0f };

            var result = VectorMath.WeightedSum(vectors, weights);

            result.Should().HaveCount(3);
            result[0].Should().Be(2.0f);
            result[1].Should().Be(4.0f);
            result[2].Should().Be(6.0f);
        }

        [Fact]
        public void Normalize_ZeroVector_DoesNotModifyOriginal()
        {
            var original = new float[] { 0, 0, 0 };

            var result = VectorMath.Normalize(original);

            result.Should().Equal(0, 0, 0);
            result.Should().NotBeSameAs(original); // Should be a new instance
            original[0] = 5; // Modifying original should not affect result
            result[0].Should().Be(0);
        }
    }
}
