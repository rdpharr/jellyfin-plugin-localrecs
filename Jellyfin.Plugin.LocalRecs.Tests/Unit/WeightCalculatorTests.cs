using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Utilities;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    public class WeightCalculatorTests
    {
        [Fact]
        public void ExponentialDecay_ZeroDays_ReturnsOne()
        {
            var result = WeightCalculator.ExponentialDecay(daysSince: 0, halfLifeDays: 365);

            result.Should().BeApproximately(1.0f, 0.0001f);
        }

        [Fact]
        public void ExponentialDecay_AtHalfLife_ReturnsHalf()
        {
            var result = WeightCalculator.ExponentialDecay(daysSince: 365, halfLifeDays: 365);

            result.Should().BeApproximately(0.5f, 0.0001f);
        }

        [Fact]
        public void ExponentialDecay_TwoHalfLives_ReturnsQuarter()
        {
            var result = WeightCalculator.ExponentialDecay(daysSince: 730, halfLifeDays: 365);

            result.Should().BeApproximately(0.25f, 0.0001f);
        }

        [Fact]
        public void ExponentialDecay_NegativeDays_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ExponentialDecay(daysSince: -1, halfLifeDays: 365);

            act.Should().Throw<ArgumentException>().WithParameterName("daysSince");
        }

        [Fact]
        public void ExponentialDecay_ZeroHalfLife_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ExponentialDecay(daysSince: 10, halfLifeDays: 0);

            act.Should().Throw<ArgumentException>().WithParameterName("halfLifeDays");
        }

        [Fact]
        public void ExponentialDecay_NegativeHalfLife_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ExponentialDecay(daysSince: 10, halfLifeDays: -365);

            act.Should().Throw<ArgumentException>().WithParameterName("halfLifeDays");
        }

        [Fact]
        public void ApplyFavoriteBoost_IsFavorite_AppliesBoost()
        {
            var result = WeightCalculator.ApplyFavoriteBoost(baseWeight: 1.0f, isFavorite: true, favoriteBoost: 2.0f);

            result.Should().Be(2.0f);
        }

        [Fact]
        public void ApplyFavoriteBoost_NotFavorite_ReturnsBaseWeight()
        {
            var result = WeightCalculator.ApplyFavoriteBoost(baseWeight: 1.0f, isFavorite: false, favoriteBoost: 2.0f);

            result.Should().Be(1.0f);
        }

        [Fact]
        public void ApplyFavoriteBoost_NegativeBaseWeight_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyFavoriteBoost(baseWeight: -1.0f, isFavorite: true, favoriteBoost: 2.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("baseWeight");
        }

        [Fact]
        public void ApplyFavoriteBoost_NegativeFavoriteBoost_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyFavoriteBoost(baseWeight: 1.0f, isFavorite: true, favoriteBoost: -2.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("favoriteBoost");
        }

        [Fact]
        public void ApplyRewatchBoost_SingleWatch_ReturnsBaseWeight()
        {
            var result = WeightCalculator.ApplyRewatchBoost(baseWeight: 1.0f, playCount: 1, rewatchBase: 1.5f);

            result.Should().Be(1.0f);
        }

        [Fact]
        public void ApplyRewatchBoost_MultipleWatches_AppliesLogarithmicBoost()
        {
            var result = WeightCalculator.ApplyRewatchBoost(baseWeight: 1.0f, playCount: 3, rewatchBase: 1.5f);

            // log_1.5(3) ≈ 2.71, so boost = 1 + 2.71 = 3.71
            result.Should().BeGreaterThan(1.0f);
            result.Should().BeApproximately(3.71f, 0.1f);
        }

        [Fact]
        public void ApplyRewatchBoost_NegativeBaseWeight_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRewatchBoost(baseWeight: -1.0f, playCount: 2, rewatchBase: 1.5f);

            act.Should().Throw<ArgumentException>().WithParameterName("baseWeight");
        }

        [Fact]
        public void ApplyRewatchBoost_ZeroPlayCount_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRewatchBoost(baseWeight: 1.0f, playCount: 0, rewatchBase: 1.5f);

            act.Should().Throw<ArgumentException>().WithParameterName("playCount");
        }

        [Fact]
        public void ApplyRewatchBoost_RewatchBaseOne_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRewatchBoost(baseWeight: 1.0f, playCount: 2, rewatchBase: 1.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("rewatchBase");
        }

        [Fact]
        public void ApplyRewatchBoost_RewatchBaseLessThanOne_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRewatchBoost(baseWeight: 1.0f, playCount: 2, rewatchBase: 0.5f);

            act.Should().Throw<ArgumentException>().WithParameterName("rewatchBase");
        }

        [Fact]
        public void ComputeCombinedWeight_AllFactors_ReturnsCompoundedWeight()
        {
            var result = WeightCalculator.ComputeCombinedWeight(
                daysSince: 365,
                halfLifeDays: 365,
                isFavorite: true,
                favoriteBoost: 2.0f,
                playCount: 2,
                rewatchBase: 1.5f);

            // decay = 0.5, favorite = 0.5 * 2 = 1.0, rewatch = 1.0 * (1 + log_1.5(2)) ≈ 1.0 * 2.71 ≈ 2.71
            result.Should().BeGreaterThan(1.0f);
        }

        [Fact]
        public void ComputeCombinedWeight_NotFavoriteSingleWatch_AppliesOnlyDecay()
        {
            var result = WeightCalculator.ComputeCombinedWeight(
                daysSince: 365,
                halfLifeDays: 365,
                isFavorite: false,
                favoriteBoost: 2.0f,
                playCount: 1,
                rewatchBase: 1.5f);

            result.Should().BeApproximately(0.5f, 0.0001f); // Only decay applies
        }

    }
}
