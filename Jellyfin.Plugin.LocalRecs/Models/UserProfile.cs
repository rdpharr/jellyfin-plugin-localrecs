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
    }
}
