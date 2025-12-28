using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Models
{
    /// <summary>
    /// Represents the vocabulary of features (genres, actors, directors, tags, collections) in the library.
    /// </summary>
    public class FeatureVocabulary
    {
        private readonly Dictionary<string, int> _genres;
        private readonly Dictionary<string, int> _actors;
        private readonly Dictionary<string, int> _directors;
        private readonly Dictionary<string, int> _tags;
        private readonly Dictionary<string, int> _decades;
        private readonly Dictionary<string, int> _collections;
        private readonly Dictionary<string, float> _genreIdf;
        private readonly Dictionary<string, float> _actorIdf;
        private readonly Dictionary<string, float> _directorIdf;
        private readonly Dictionary<string, float> _tagIdf;
        private readonly Dictionary<string, float> _decadeIdf;

        /// <summary>
        /// Initializes a new instance of the <see cref="FeatureVocabulary"/> class.
        /// </summary>
        public FeatureVocabulary()
        {
            _genres = new Dictionary<string, int>();
            _actors = new Dictionary<string, int>();
            _directors = new Dictionary<string, int>();
            _tags = new Dictionary<string, int>();
            _decades = new Dictionary<string, int>();
            _collections = new Dictionary<string, int>();
            _genreIdf = new Dictionary<string, float>();
            _actorIdf = new Dictionary<string, float>();
            _directorIdf = new Dictionary<string, float>();
            _tagIdf = new Dictionary<string, float>();
            _decadeIdf = new Dictionary<string, float>();
        }

        /// <summary>
        /// Gets the genre vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Genres => _genres;

        /// <summary>
        /// Gets the actor vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Actors => _actors;

        /// <summary>
        /// Gets the director vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Directors => _directors;

        /// <summary>
        /// Gets the tag vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Tags => _tags;

        /// <summary>
        /// Gets the decade vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Decades => _decades;

        /// <summary>
        /// Gets the collection vocabulary (feature name → document frequency).
        /// </summary>
        public IReadOnlyDictionary<string, int> Collections => _collections;

        /// <summary>
        /// Gets the IDF (Inverse Document Frequency) values for genres.
        /// </summary>
        public IReadOnlyDictionary<string, float> GenreIdf => _genreIdf;

        /// <summary>
        /// Gets the IDF values for actors.
        /// </summary>
        public IReadOnlyDictionary<string, float> ActorIdf => _actorIdf;

        /// <summary>
        /// Gets the IDF values for directors.
        /// </summary>
        public IReadOnlyDictionary<string, float> DirectorIdf => _directorIdf;

        /// <summary>
        /// Gets the IDF values for tags.
        /// </summary>
        public IReadOnlyDictionary<string, float> TagIdf => _tagIdf;

        /// <summary>
        /// Gets the IDF values for decades.
        /// </summary>
        public IReadOnlyDictionary<string, float> DecadeIdf => _decadeIdf;

        /// <summary>
        /// Gets or sets the total number of items in the library.
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this vocabulary was built.
        /// </summary>
        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets the total vocabulary size (all features combined).
        /// </summary>
        public int TotalFeatures =>
            Genres.Count + Actors.Count + Directors.Count + Tags.Count + Decades.Count + Collections.Count;

        /// <summary>
        /// Adds or updates a genre in the vocabulary.
        /// </summary>
        /// <param name="genre">The genre name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddGenre(string genre, int documentFrequency) => _genres[genre] = documentFrequency;

        /// <summary>
        /// Adds or updates an actor in the vocabulary.
        /// </summary>
        /// <param name="actor">The actor name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddActor(string actor, int documentFrequency) => _actors[actor] = documentFrequency;

        /// <summary>
        /// Adds or updates a director in the vocabulary.
        /// </summary>
        /// <param name="director">The director name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddDirector(string director, int documentFrequency) => _directors[director] = documentFrequency;

        /// <summary>
        /// Adds or updates a tag in the vocabulary.
        /// </summary>
        /// <param name="tag">The tag name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddTag(string tag, int documentFrequency) => _tags[tag] = documentFrequency;

        /// <summary>
        /// Adds or updates a decade in the vocabulary.
        /// </summary>
        /// <param name="decade">The decade name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddDecade(string decade, int documentFrequency) => _decades[decade] = documentFrequency;

        /// <summary>
        /// Adds or updates a collection in the vocabulary.
        /// </summary>
        /// <param name="collection">The collection name.</param>
        /// <param name="documentFrequency">The document frequency.</param>
        public void AddCollection(string collection, int documentFrequency) => _collections[collection] = documentFrequency;

        /// <summary>
        /// Sets the IDF value for a genre.
        /// </summary>
        /// <param name="genre">The genre name.</param>
        /// <param name="idf">The IDF value.</param>
        public void SetGenreIdf(string genre, float idf) => _genreIdf[genre] = idf;

        /// <summary>
        /// Sets the IDF value for an actor.
        /// </summary>
        /// <param name="actor">The actor name.</param>
        /// <param name="idf">The IDF value.</param>
        public void SetActorIdf(string actor, float idf) => _actorIdf[actor] = idf;

        /// <summary>
        /// Sets the IDF value for a director.
        /// </summary>
        /// <param name="director">The director name.</param>
        /// <param name="idf">The IDF value.</param>
        public void SetDirectorIdf(string director, float idf) => _directorIdf[director] = idf;

        /// <summary>
        /// Sets the IDF value for a tag.
        /// </summary>
        /// <param name="tag">The tag name.</param>
        /// <param name="idf">The IDF value.</param>
        public void SetTagIdf(string tag, float idf) => _tagIdf[tag] = idf;

        /// <summary>
        /// Sets the IDF value for a decade.
        /// </summary>
        /// <param name="decade">The decade name.</param>
        /// <param name="idf">The IDF value.</param>
        public void SetDecadeIdf(string decade, float idf) => _decadeIdf[decade] = idf;
    }
}
