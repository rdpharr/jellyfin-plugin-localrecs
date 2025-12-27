using System;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents a user's taste profile (aggregated embedding vector from watch history).
    /// </summary>
    public class UserProfile
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserProfile"/> class.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="tasteVector">The taste vector (normalized weighted sum of item embeddings).</param>
        public UserProfile(Guid userId, float[] tasteVector)
        {
            UserId = userId;
            TasteVector = tasteVector ?? throw new ArgumentNullException(nameof(tasteVector));
        }

        /// <summary>
        /// Gets the user identifier.
        /// </summary>
        public Guid UserId { get; }

        /// <summary>
        /// Gets the user's taste vector (normalized weighted sum of watched item embeddings).
        /// </summary>
        public float[] TasteVector { get; }

        /// <summary>
        /// Gets or sets the number of items used to build this profile.
        /// </summary>
        public int WatchedItemCount { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this profile was computed.
        /// </summary>
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets the dimensionality of the taste vector.
        /// </summary>
        public int Dimensions => TasteVector.Length;

        /// <summary>
        /// Gets or sets the average community rating (0-10 scale) from watched items.
        /// Null if user has no watched items with community ratings.
        /// </summary>
        public float? AverageCommunityRating { get; set; }

        /// <summary>
        /// Gets or sets the average critic rating (0-100 scale) from watched items.
        /// Null if user has no watched items with critic ratings.
        /// </summary>
        public float? AverageCriticRating { get; set; }

        /// <summary>
        /// Gets or sets the standard deviation of community ratings from watched items.
        /// </summary>
        public float CommunityRatingStdDev { get; set; }

        /// <summary>
        /// Gets or sets the standard deviation of critic ratings from watched items.
        /// </summary>
        public float CriticRatingStdDev { get; set; }
    }
}
