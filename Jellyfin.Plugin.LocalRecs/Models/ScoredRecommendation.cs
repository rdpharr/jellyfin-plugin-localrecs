using System;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents a ranked recommendation candidate with similarity score.
    /// </summary>
    public class ScoredRecommendation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScoredRecommendation"/> class.
        /// </summary>
        /// <param name="itemId">The item identifier.</param>
        /// <param name="score">The similarity score.</param>
        public ScoredRecommendation(Guid itemId, float score)
        {
            ItemId = itemId;
            Score = score;
        }

        /// <summary>
        /// Gets the item identifier.
        /// </summary>
        public Guid ItemId { get; }

        /// <summary>
        /// Gets the cosine similarity score (higher is better, range 0-1).
        /// </summary>
        public float Score { get; }
    }
}
