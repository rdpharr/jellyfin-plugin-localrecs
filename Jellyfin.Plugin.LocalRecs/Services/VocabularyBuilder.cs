using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for building genre, actor, director, tag, and decade vocabularies from media library.
    /// Vocabularies are used to compute IDF values for TF-IDF embeddings.
    /// </summary>
    public class VocabularyBuilder
    {
        private readonly ILogger<VocabularyBuilder> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="VocabularyBuilder"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public VocabularyBuilder(ILogger<VocabularyBuilder> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Builds a feature vocabulary from a collection of media items.
        /// </summary>
        /// <param name="items">The media items.</param>
        /// <param name="maxActors">Maximum number of actors to include (0 = unlimited).</param>
        /// <param name="maxDirectors">Maximum number of directors to include (0 = unlimited).</param>
        /// <param name="maxTags">Maximum number of tags to include (0 = unlimited).</param>
        /// <returns>FeatureVocabulary with all features and IDF values.</returns>
        public FeatureVocabulary BuildVocabulary(
            IEnumerable<MediaItemMetadata> items,
            int maxActors = 0,
            int maxDirectors = 0,
            int maxTags = 0)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var itemList = items.ToList();
            if (itemList.Count == 0)
            {
                _logger.LogWarning("No items provided to build vocabulary");
                return new FeatureVocabulary();
            }

            _logger.LogInformation("Building vocabulary from {Count} items", itemList.Count);

            var vocabulary = new FeatureVocabulary();

            // Build genre vocabulary and document frequencies
            var genreDocCounts = BuildFeatureDocumentCounts(itemList, item => item.Genres);
            foreach (var genre in genreDocCounts.Keys)
            {
                vocabulary.AddGenre(genre, genreDocCounts[genre]);
                var idf = ComputeIdf(itemList.Count, genreDocCounts[genre]);
                vocabulary.SetGenreIdf(genre, idf);
            }

            // Build actor vocabulary and document frequencies
            var actorDocCounts = BuildFeatureDocumentCounts(itemList, item => item.Actors);

            // Limit vocabulary size if requested
            var topActors = maxActors > 0
                ? actorDocCounts.OrderByDescending(kvp => kvp.Value).Take(maxActors)
                : actorDocCounts;

            foreach (var actor in topActors)
            {
                vocabulary.AddActor(actor.Key, actor.Value);
                var idf = ComputeIdf(itemList.Count, actor.Value);
                vocabulary.SetActorIdf(actor.Key, idf);
            }

            // Build director vocabulary and document frequencies
            var directorDocCounts = BuildFeatureDocumentCounts(itemList, item => item.Directors);

            // Limit vocabulary size if requested
            var topDirectors = maxDirectors > 0
                ? directorDocCounts.OrderByDescending(kvp => kvp.Value).Take(maxDirectors)
                : directorDocCounts;

            foreach (var director in topDirectors)
            {
                vocabulary.AddDirector(director.Key, director.Value);
                var idf = ComputeIdf(itemList.Count, director.Value);
                vocabulary.SetDirectorIdf(director.Key, idf);
            }

            // Build tag vocabulary and document frequencies
            var tagDocCounts = BuildFeatureDocumentCounts(itemList, item => item.Tags);

            // Limit vocabulary size if requested
            var topTags = maxTags > 0
                ? tagDocCounts.OrderByDescending(kvp => kvp.Value).Take(maxTags)
                : tagDocCounts;

            foreach (var tag in topTags)
            {
                vocabulary.AddTag(tag.Key, tag.Value);
                var idf = ComputeIdf(itemList.Count, tag.Value);
                vocabulary.SetTagIdf(tag.Key, idf);
            }

            // Build decade vocabulary and document frequencies
            var decadeDocCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in itemList)
            {
                var decade = item.Decade;
                if (decadeDocCounts.ContainsKey(decade))
                {
                    decadeDocCounts[decade]++;
                }
                else
                {
                    decadeDocCounts[decade] = 1;
                }
            }

            foreach (var decade in decadeDocCounts)
            {
                vocabulary.AddDecade(decade.Key, decade.Value);
                var idf = ComputeIdf(itemList.Count, decade.Value);
                vocabulary.SetDecadeIdf(decade.Key, idf);
            }

            // Set metadata
            vocabulary.TotalItems = itemList.Count;

            _logger.LogInformation(
                "Built vocabulary: {GenreCount} genres, {ActorCount} actors, {DirectorCount} directors, {TagCount} tags, {DecadeCount} decades",
                vocabulary.Genres.Count,
                vocabulary.Actors.Count,
                vocabulary.Directors.Count,
                vocabulary.Tags.Count,
                vocabulary.Decades.Count);

            return vocabulary;
        }

        /// <summary>
        /// Builds document frequency counts for a specific feature type.
        /// Document frequency = number of items that contain the feature.
        /// </summary>
        /// <param name="items">The media items.</param>
        /// <param name="featureSelector">Function to extract features from an item.</param>
        /// <returns>Dictionary mapping feature to document count.</returns>
        private Dictionary<string, int> BuildFeatureDocumentCounts(
            IEnumerable<MediaItemMetadata> items,
            Func<MediaItemMetadata, IReadOnlyList<string>> featureSelector)
        {
            var docCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var features = featureSelector(item);
                var uniqueFeatures = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);

                foreach (var feature in uniqueFeatures)
                {
                    if (string.IsNullOrWhiteSpace(feature))
                    {
                        continue;
                    }

                    if (docCounts.ContainsKey(feature))
                    {
                        docCounts[feature]++;
                    }
                    else
                    {
                        docCounts[feature] = 1;
                    }
                }
            }

            return docCounts;
        }

        /// <summary>
        /// Computes Inverse Document Frequency (IDF) for a feature.
        /// IDF = log(total_documents / documents_containing_feature).
        /// </summary>
        /// <param name="totalDocuments">Total number of documents.</param>
        /// <param name="documentsContainingFeature">Number of documents containing the feature.</param>
        /// <returns>IDF value.</returns>
        private float ComputeIdf(int totalDocuments, int documentsContainingFeature)
        {
            if (documentsContainingFeature == 0)
            {
                return 0;
            }

            return (float)Math.Log((double)totalDocuments / documentsContainingFeature);
        }
    }
}
