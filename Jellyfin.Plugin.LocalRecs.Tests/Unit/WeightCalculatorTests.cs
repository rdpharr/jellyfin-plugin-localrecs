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
        public void ApplyRecentWatchBoost_ZeroBoost_ReturnsDecay()
        {
            var result = WeightCalculator.ApplyRecentWatchBoost(decay: 0.5f, recentWatchBoost: 0.0f);

            result.Should().BeApproximately(0.5f, 0.0001f);
        }

        [Fact]
        public void ApplyRecentWatchBoost_JustWatchedWithBoostOne_DoublesWeight()
        {
            // decay=1 (just watched), boost=1 → 1 × (1 + 1×1) = 2
            var result = WeightCalculator.ApplyRecentWatchBoost(decay: 1.0f, recentWatchBoost: 1.0f);

            result.Should().BeApproximately(2.0f, 0.0001f);
        }

        [Fact]
        public void ApplyRecentWatchBoost_OldItemsLessAmplified()
        {
            var recentResult = WeightCalculator.ApplyRecentWatchBoost(decay: 1.0f, recentWatchBoost: 1.0f);
            var oldResult = WeightCalculator.ApplyRecentWatchBoost(decay: 0.5f, recentWatchBoost: 1.0f);

            recentResult.Should().BeGreaterThan(oldResult);
        }

        [Fact]
        public void ApplyRecentWatchBoost_NegativeDecay_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRecentWatchBoost(decay: -0.1f, recentWatchBoost: 1.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("decay");
        }

        [Fact]
        public void ApplyRecentWatchBoost_DecayAboveOne_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRecentWatchBoost(decay: 1.1f, recentWatchBoost: 1.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("decay");
        }

        [Fact]
        public void ApplyRecentWatchBoost_NegativeBoost_ThrowsArgumentException()
        {
            Action act = () => WeightCalculator.ApplyRecentWatchBoost(decay: 0.5f, recentWatchBoost: -1.0f);

            act.Should().Throw<ArgumentException>().WithParameterName("recentWatchBoost");
        }

        [Fact]
        public void ComputeCombinedWeight_WithBoostAndFavorite_ReturnsCompoundedWeight()
        {
            // decay = 0.5, boost: 0.5×(1+1×0.5)=0.75, favorite: 0.75×2=1.5
            var result = WeightCalculator.ComputeCombinedWeight(
                daysSince: 365,
                halfLifeDays: 365,
                isFavorite: true,
                favoriteBoost: 2.0f,
                recentWatchBoost: 1.0f);

            result.Should().BeApproximately(1.5f, 0.001f);
        }

        [Fact]
        public void ComputeCombinedWeight_ZeroBoostNotFavorite_AppliesOnlyDecaySquared()
        {
            // decay=0.5, boost=0: 0.5×(1+0)=0.5
            var result = WeightCalculator.ComputeCombinedWeight(
                daysSince: 365,
                halfLifeDays: 365,
                isFavorite: false,
                favoriteBoost: 2.0f,
                recentWatchBoost: 0.0f);

            result.Should().BeApproximately(0.5f, 0.0001f);
        }

        [Fact]
        public void ComputeCombinedWeight_JustWatchedWithBoost_AmplifiedWeight()
        {
            // daysSince=0 → decay=1, boost=1: 1×(1+1×1)=2
            var result = WeightCalculator.ComputeCombinedWeight(
                daysSince: 0,
                halfLifeDays: 365,
                isFavorite: false,
                favoriteBoost: 2.0f,
                recentWatchBoost: 1.0f);

            result.Should().BeApproximately(2.0f, 0.0001f);
        }

    }
}
