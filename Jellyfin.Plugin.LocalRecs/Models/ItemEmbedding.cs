using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents a TF-IDF embedding vector for a media item.
    /// </summary>
    public class ItemEmbedding
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ItemEmbedding"/> class.
        /// Required for JSON deserialization.
        /// </summary>
        [JsonConstructor]
        public ItemEmbedding()
        {
            Vector = Array.Empty<float>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemEmbedding"/> class.
        /// </summary>
        /// <param name="itemId">The item identifier.</param>
        /// <param name="vector">The embedding vector.</param>
        public ItemEmbedding(Guid itemId, float[] vector)
        {
            ItemId = itemId;
            Vector = vector ?? throw new ArgumentNullException(nameof(vector));
        }

        /// <summary>
        /// Gets the item identifier.
        /// </summary>
        public Guid ItemId { get; init; }

        /// <summary>
        /// Gets the embedding vector.
        /// Vector format: [genre_tfidf | actor_tfidf | director_tfidf | tag_tfidf | ratings | year].
        /// </summary>
        public float[] Vector { get; init; }

        /// <summary>
        /// Gets the dimensionality of the embedding vector.
        /// </summary>
        [JsonIgnore]
        public int Dimensions => Vector.Length;

        /// <summary>
        /// Gets or sets the timestamp when this embedding was computed.
        /// </summary>
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }
}
