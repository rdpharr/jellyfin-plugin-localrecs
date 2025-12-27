using System;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents a user's watch record for a media item.
    /// </summary>
    public class WatchRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WatchRecord"/> class.
        /// </summary>
        /// <param name="itemId">The item identifier.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="lastPlayedDate">The last played date.</param>
        public WatchRecord(Guid itemId, Guid userId, DateTime lastPlayedDate)
        {
            ItemId = itemId;
            UserId = userId;
            LastPlayedDate = lastPlayedDate;
        }

        /// <summary>
        /// Gets the item identifier.
        /// </summary>
        public Guid ItemId { get; }

        /// <summary>
        /// Gets the user identifier.
        /// </summary>
        public Guid UserId { get; }

        /// <summary>
        /// Gets the last played date.
        /// </summary>
        public DateTime LastPlayedDate { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the item is marked as favorite.
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets the play count.
        /// </summary>
        public int PlayCount { get; set; }

        /// <summary>
        /// Gets or sets the community rating (0-10 scale) of the item, if available.
        /// </summary>
        public float? CommunityRating { get; set; }

        /// <summary>
        /// Gets or sets the critic rating (0-100 scale) of the item, if available.
        /// </summary>
        public float? CriticRating { get; set; }
    }
}
