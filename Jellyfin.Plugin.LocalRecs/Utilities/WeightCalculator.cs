using System;

namespace Jellyfin.Plugin.LocalRecs.Utilities
{
    /// <summary>
    /// Pure functions for computing weights (recency decay, favorite boost, rewatch boost).
    /// </summary>
    public static class WeightCalculator
    {
        /// <summary>
        /// Computes exponential recency decay weight.
        /// Weight = 0.5 ^ (days_since / half_life).
        /// </summary>
        /// <param name="daysSince">Number of days since the event.</param>
        /// <param name="halfLifeDays">Half-life in days (time for weight to decay to 50%).</param>
        /// <returns>Decay weight (0 to 1).</returns>
        /// <exception cref="ArgumentException">Thrown when daysSince is negative or halfLifeDays is less than or equal to 0.</exception>
        public static float ExponentialDecay(double daysSince, double halfLifeDays)
        {
            if (daysSince < 0)
            {
                throw new ArgumentException("Days since cannot be negative", nameof(daysSince));
            }

            if (halfLifeDays <= 0)
            {
                throw new ArgumentException("Half-life must be greater than 0", nameof(halfLifeDays));
            }

            // Weight = 0.5 ^ (days_since / half_life)
            return (float)Math.Pow(0.5, daysSince / halfLifeDays);
        }

        /// <summary>
        /// Applies favorite boost multiplier.
        /// </summary>
        /// <param name="baseWeight">The base weight.</param>
        /// <param name="isFavorite">Whether the item is marked as favorite.</param>
        /// <param name="favoriteBoost">Multiplier for favorites.</param>
        /// <returns>Boosted weight.</returns>
        /// <exception cref="ArgumentException">Thrown when baseWeight or favoriteBoost is negative.</exception>
        public static float ApplyFavoriteBoost(float baseWeight, bool isFavorite, float favoriteBoost)
        {
            if (baseWeight < 0)
            {
                throw new ArgumentException("Base weight cannot be negative", nameof(baseWeight));
            }

            if (favoriteBoost < 0)
            {
                throw new ArgumentException("Favorite boost cannot be negative", nameof(favoriteBoost));
            }

            return isFavorite ? baseWeight * favoriteBoost : baseWeight;
        }

        /// <summary>
        /// Applies rewatch boost using logarithmic scaling.
        /// Boost = base_weight × (1 + log(play_count, rewatch_base)).
        /// </summary>
        /// <param name="baseWeight">The base weight.</param>
        /// <param name="playCount">Number of times the item was played.</param>
        /// <param name="rewatchBase">Base for logarithmic scaling (default 1.5).</param>
        /// <returns>Boosted weight.</returns>
        /// <exception cref="ArgumentException">Thrown when baseWeight is negative, playCount is less than 1, or rewatchBase is less than or equal to 1.</exception>
        public static float ApplyRewatchBoost(float baseWeight, int playCount, float rewatchBase = 1.5f)
        {
            if (baseWeight < 0)
            {
                throw new ArgumentException("Base weight cannot be negative", nameof(baseWeight));
            }

            if (playCount < 1)
            {
                throw new ArgumentException("Play count must be at least 1", nameof(playCount));
            }

            if (rewatchBase <= 1)
            {
                throw new ArgumentException("Rewatch base must be greater than 1", nameof(rewatchBase));
            }

            if (playCount == 1)
            {
                return baseWeight; // No boost for single watch
            }

            // Logarithmic scaling: 1 + log_base(play_count)
            float boost = 1.0f + (float)Math.Log(playCount, rewatchBase);
            return baseWeight * boost;
        }

        /// <summary>
        /// Computes the combined weight for a watch record.
        /// </summary>
        /// <param name="daysSince">Days since last watched.</param>
        /// <param name="halfLifeDays">Recency decay half-life.</param>
        /// <param name="isFavorite">Whether the item is favorite.</param>
        /// <param name="favoriteBoost">Favorite boost multiplier.</param>
        /// <param name="playCount">Number of times played.</param>
        /// <param name="rewatchBase">Rewatch logarithmic base.</param>
        /// <returns>Combined weight.</returns>
        public static float ComputeCombinedWeight(
            double daysSince,
            double halfLifeDays,
            bool isFavorite,
            float favoriteBoost,
            int playCount,
            float rewatchBase = 1.5f)
        {
            float weight = ExponentialDecay(daysSince, halfLifeDays);
            weight = ApplyFavoriteBoost(weight, isFavorite, favoriteBoost);
            weight = ApplyRewatchBoost(weight, playCount, rewatchBase);
            return weight;
        }
    }
}
