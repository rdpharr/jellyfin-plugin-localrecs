using System;
using System.Collections.Generic;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    public class TfIdfComputerTests
    {
        [Fact]
        public void ComputeIdf_CommonFeature_ReturnsLowIdf()
        {
            var result = TfIdfComputer.ComputeIdf(totalDocuments: 100, documentsWithFeature: 50);

            result.Should().BeApproximately(0.693f, 0.001f); // log(100/50) ≈ 0.693
        }

        [Fact]
        public void ComputeIdf_RareFeature_ReturnsHighIdf()
        {
            var result = TfIdfComputer.ComputeIdf(totalDocuments: 100, documentsWithFeature: 1);

            result.Should().BeApproximately(4.605f, 0.001f); // log(100/1) ≈ 4.605
        }

        [Fact]
        public void ComputeIdf_UniversalFeature_ReturnsZero()
        {
            var result = TfIdfComputer.ComputeIdf(totalDocuments: 100, documentsWithFeature: 100);

            result.Should().BeApproximately(0.0f, 0.001f); // log(100/100) = 0
        }

        [Fact]
        public void ComputeIdf_ZeroTotalDocuments_ThrowsArgumentException()
        {
            Action act = () => TfIdfComputer.ComputeIdf(totalDocuments: 0, documentsWithFeature: 1);

            act.Should().Throw<ArgumentException>().WithParameterName("totalDocuments");
        }

        [Fact]
        public void ComputeIdf_ZeroDocumentsWithFeature_ThrowsArgumentException()
        {
            Action act = () => TfIdfComputer.ComputeIdf(totalDocuments: 100, documentsWithFeature: 0);

            act.Should().Throw<ArgumentException>().WithParameterName("documentsWithFeature");
        }

        [Fact]
        public void ComputeIdf_MoreDocumentsWithFeatureThanTotal_ThrowsArgumentException()
        {
            Action act = () => TfIdfComputer.ComputeIdf(totalDocuments: 100, documentsWithFeature: 101);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void ComputeTfIdf_ReturnsCorrectScores()
        {
            var documentFeatures = new[] { "action", "scifi" };
            var idfValues = new Dictionary<string, float>
            {
                { "action", 0.5f },
                { "scifi", 1.0f },
                { "drama", 0.7f },
            };

            var result = TfIdfComputer.ComputeTfIdf(documentFeatures, idfValues);

            result.Should().HaveCount(2);
            result["action"].Should().Be(0.5f); // TF=1, IDF=0.5
            result["scifi"].Should().Be(1.0f);  // TF=1, IDF=1.0
            result.Should().NotContainKey("drama");
        }

        [Fact]
        public void ComputeTfIdf_FeatureNotInVocabulary_Ignored()
        {
            var documentFeatures = new[] { "action", "unknown" };
            var idfValues = new Dictionary<string, float>
            {
                { "action", 0.5f },
            };

            var result = TfIdfComputer.ComputeTfIdf(documentFeatures, idfValues);

            result.Should().HaveCount(1);
            result.Should().ContainKey("action");
            result.Should().NotContainKey("unknown");
        }

        [Fact]
        public void ComputeTfIdf_NullDocumentFeatures_ThrowsArgumentNullException()
        {
            var idfValues = new Dictionary<string, float>();

            Action act = () => TfIdfComputer.ComputeTfIdf(null!, idfValues);

            act.Should().Throw<ArgumentNullException>().WithParameterName("documentFeatures");
        }

        [Fact]
        public void ComputeTfIdf_NullIdfValues_ThrowsArgumentNullException()
        {
            var documentFeatures = new[] { "action" };

            Action act = () => TfIdfComputer.ComputeTfIdf(documentFeatures, null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("idfValues");
        }

        [Fact]
        public void ComputeTfIdf_EmptyDocumentFeatures_ReturnsEmptyDictionary()
        {
            var documentFeatures = Array.Empty<string>();
            var idfValues = new Dictionary<string, float>
            {
                { "action", 0.5f },
            };

            var result = TfIdfComputer.ComputeTfIdf(documentFeatures, idfValues);

            result.Should().BeEmpty();
        }

        [Fact]
        public void BuildVector_ReturnsCorrectDenseVector()
        {
            var tfIdfScores = new Dictionary<string, float>
            {
                { "action", 0.5f },
                { "scifi", 1.0f },
            };
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "action", 0 },
                { "drama", 1 },
                { "scifi", 2 },
            };

            var result = TfIdfComputer.BuildVector(tfIdfScores, vocabularyIndex, vectorSize: 3);

            result.Should().HaveCount(3);
            result[0].Should().Be(0.5f); // action
            result[1].Should().Be(0.0f); // drama (not present)
            result[2].Should().Be(1.0f); // scifi
        }

        [Fact]
        public void BuildVector_FeatureNotInVocabulary_Ignored()
        {
            var tfIdfScores = new Dictionary<string, float>
            {
                { "action", 0.5f },
                { "unknown", 2.0f },
            };
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "action", 0 },
            };

            var result = TfIdfComputer.BuildVector(tfIdfScores, vocabularyIndex, vectorSize: 1);

            result.Should().Equal(0.5f);
        }

        [Fact]
        public void BuildVector_NullTfIdfScores_ThrowsArgumentNullException()
        {
            var vocabularyIndex = new Dictionary<string, int>();

            Action act = () => TfIdfComputer.BuildVector(null!, vocabularyIndex, vectorSize: 3);

            act.Should().Throw<ArgumentNullException>().WithParameterName("tfIdfScores");
        }

        [Fact]
        public void BuildVector_NullVocabularyIndex_ThrowsArgumentNullException()
        {
            var tfIdfScores = new Dictionary<string, float>();

            Action act = () => TfIdfComputer.BuildVector(tfIdfScores, null!, vectorSize: 3);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vocabularyIndex");
        }

        [Fact]
        public void BuildVector_ZeroVectorSize_ThrowsArgumentException()
        {
            var tfIdfScores = new Dictionary<string, float>();
            var vocabularyIndex = new Dictionary<string, int>();

            Action act = () => TfIdfComputer.BuildVector(tfIdfScores, vocabularyIndex, vectorSize: 0);

            act.Should().Throw<ArgumentException>().WithParameterName("vectorSize");
        }

        [Fact]
        public void NormalizeScalar_ValueInRange_ReturnsNormalized()
        {
            var result = TfIdfComputer.NormalizeScalar(value: 5.0f, min: 0.0f, max: 10.0f);

            result.Should().Be(0.5f);
        }

        [Fact]
        public void NormalizeScalar_ValueAtMin_ReturnsZero()
        {
            var result = TfIdfComputer.NormalizeScalar(value: 0.0f, min: 0.0f, max: 10.0f);

            result.Should().Be(0.0f);
        }

        [Fact]
        public void NormalizeScalar_ValueAtMax_ReturnsOne()
        {
            var result = TfIdfComputer.NormalizeScalar(value: 10.0f, min: 0.0f, max: 10.0f);

            result.Should().Be(1.0f);
        }

        [Fact]
        public void NormalizeScalar_ValueBelowMin_ReturnsZero()
        {
            var result = TfIdfComputer.NormalizeScalar(value: -5.0f, min: 0.0f, max: 10.0f);

            result.Should().Be(0.0f);
        }

        [Fact]
        public void NormalizeScalar_ValueAboveMax_ReturnsOne()
        {
            var result = TfIdfComputer.NormalizeScalar(value: 15.0f, min: 0.0f, max: 10.0f);

            result.Should().Be(1.0f);
        }

        [Fact]
        public void NormalizeScalar_MaxLessThanOrEqualMin_ThrowsArgumentException()
        {
            Action act = () => TfIdfComputer.NormalizeScalar(value: 5.0f, min: 10.0f, max: 10.0f);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void OneHotEncode_ValueInVocabulary_ReturnsOneHotVector()
        {
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "a", 0 },
                { "b", 1 },
                { "c", 2 },
            };

            var result = TfIdfComputer.OneHotEncode("b", vocabularyIndex, vocabularySize: 3);

            result.Should().Equal(0, 1, 0);
        }

        [Fact]
        public void OneHotEncode_ValueNotInVocabulary_ReturnsZeroVector()
        {
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "a", 0 },
                { "b", 1 },
            };

            var result = TfIdfComputer.OneHotEncode("c", vocabularyIndex, vocabularySize: 2);

            result.Should().Equal(0, 0);
        }

        [Fact]
        public void OneHotEncode_NullValue_ReturnsZeroVector()
        {
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "a", 0 },
                { "b", 1 },
            };

            var result = TfIdfComputer.OneHotEncode(null, vocabularyIndex, vocabularySize: 2);

            result.Should().Equal(0, 0);
        }

        [Fact]
        public void OneHotEncode_NullVocabularyIndex_ThrowsArgumentNullException()
        {
            Action act = () => TfIdfComputer.OneHotEncode("a", null!, vocabularySize: 2);

            act.Should().Throw<ArgumentNullException>().WithParameterName("vocabularyIndex");
        }

        [Fact]
        public void OneHotEncode_ZeroVocabularySize_ThrowsArgumentException()
        {
            var vocabularyIndex = new Dictionary<string, int>();

            Action act = () => TfIdfComputer.OneHotEncode("a", vocabularyIndex, vocabularySize: 0);

            act.Should().Throw<ArgumentException>().WithParameterName("vocabularySize");
        }

        [Fact]
        public void BuildVector_IndexOutOfBounds_ThrowsArgumentException()
        {
            var tfIdfScores = new Dictionary<string, float>
            {
                { "feature", 1.0f },
            };
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "feature", 999 }, // Out of bounds
            };

            Action act = () => TfIdfComputer.BuildVector(tfIdfScores, vocabularyIndex, vectorSize: 3);

            act.Should().Throw<ArgumentException>().WithMessage("*out of bounds*");
        }

        [Fact]
        public void BuildVector_NegativeIndex_ThrowsArgumentException()
        {
            var tfIdfScores = new Dictionary<string, float>
            {
                { "feature", 1.0f },
            };
            var vocabularyIndex = new Dictionary<string, int>
            {
                { "feature", -1 },
            };

            Action act = () => TfIdfComputer.BuildVector(tfIdfScores, vocabularyIndex, vectorSize: 3);

            act.Should().Throw<ArgumentException>().WithMessage("*out of bounds*");
        }
    }
}
