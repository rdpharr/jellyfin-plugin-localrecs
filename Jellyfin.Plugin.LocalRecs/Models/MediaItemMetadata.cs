using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents metadata for a media item (movie or TV show).
    /// This is our abstraction layer over Jellyfin's BaseItem to make testing easier.
    /// </summary>
    public class MediaItemMetadata
    {
        private readonly List<string> _genres;
        private readonly List<string> _actors;
        private readonly List<string> _directors;
        private readonly List<string> _tags;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaItemMetadata"/> class.
        /// </summary>
        /// <param name="id">The unique identifier for the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="type">The type of media (Movie or Series).</param>
        public MediaItemMetadata(Guid id, string name, MediaType type)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            _genres = new List<string>();
            _actors = new List<string>();
            _directors = new List<string>();
            _tags = new List<string>();
        }

        /// <summary>
        /// Gets the unique identifier for the item.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the name of the item.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the type of media.
        /// </summary>
        public MediaType Type { get; }

        /// <summary>
        /// Gets the list of genres.
        /// </summary>
        public IReadOnlyList<string> Genres => _genres;

        /// <summary>
        /// Gets the list of actors.
        /// </summary>
        public IReadOnlyList<string> Actors => _actors;

        /// <summary>
        /// Gets the list of directors.
        /// </summary>
        public IReadOnlyList<string> Directors => _directors;

        /// <summary>
        /// Gets the list of tags.
        /// </summary>
        public IReadOnlyList<string> Tags => _tags;

        /// <summary>
        /// Gets or sets the community rating (0-10 scale).
        /// </summary>
        public float? CommunityRating { get; set; }

        /// <summary>
        /// Gets or sets the critic rating (0-100 scale, from sources like Rotten Tomatoes).
        /// </summary>
        public float? CriticRating { get; set; }

        /// <summary>
        /// Gets or sets the release year.
        /// </summary>
        public int? ReleaseYear { get; set; }

        /// <summary>
        /// Gets the decade of release (e.g., "1980s", "1990s", "Unknown").
        /// Returns "Unknown" if ReleaseYear is not set.
        /// </summary>
        public string Decade => ReleaseYear.HasValue
            ? $"{(ReleaseYear.Value / 10) * 10}s"
            : "Unknown";

        /// <summary>
        /// Gets or sets the collection name (e.g., "Star Wars Collection").
        /// </summary>
        public string? CollectionName { get; set; }

        /// <summary>
        /// Gets or sets the TMDB ID for movies.
        /// </summary>
        public string? TmdbId { get; set; }

        /// <summary>
        /// Gets or sets the TVDB ID for TV series.
        /// </summary>
        public string? TvdbId { get; set; }

        /// <summary>
        /// Gets or sets the file path to the media item.
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// Adds a genre to the item.
        /// </summary>
        /// <param name="genre">The genre to add.</param>
        public void AddGenre(string genre) => _genres.Add(genre);

        /// <summary>
        /// Adds an actor to the item.
        /// </summary>
        /// <param name="actor">The actor to add.</param>
        public void AddActor(string actor) => _actors.Add(actor);

        /// <summary>
        /// Adds a director to the item.
        /// </summary>
        /// <param name="director">The director to add.</param>
        public void AddDirector(string director) => _directors.Add(director);

        /// <summary>
        /// Adds a tag to the item.
        /// </summary>
        /// <param name="tag">The tag to add.</param>
        public void AddTag(string tag) => _tags.Add(tag);
    }
}
