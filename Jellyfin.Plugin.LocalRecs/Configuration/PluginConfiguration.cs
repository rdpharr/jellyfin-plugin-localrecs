using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalRecs.Configuration
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            MovieRecommendationCount = 25;
            TvRecommendationCount = 25;
            FavoriteBoost = 2.0;
            RewatchBoost = 1.5;
            RecencyDecayHalfLifeDays = 365.0;
            MinWatchedItemsForPersonalization = 3;
            ExcludeAbandonedSeries = true;
            AbandonedSeriesThresholdDays = 90;
            MaxVocabularyActors = 500;
            MaxVocabularyDirectors = 0;
            MaxVocabularyTags = 500;
        }

        /// <summary>
        /// Gets or sets the number of movie recommendations to generate per user.
        /// </summary>
        public int MovieRecommendationCount { get; set; }

        /// <summary>
        /// Gets or sets the number of TV recommendations to generate per user.
        /// </summary>
        public int TvRecommendationCount { get; set; }

        /// <summary>
        /// Gets or sets the boost multiplier for favorite items.
        /// </summary>
        public double FavoriteBoost { get; set; }

        /// <summary>
        /// Gets or sets the boost multiplier for rewatched items.
        /// </summary>
        public double RewatchBoost { get; set; }

        /// <summary>
        /// Gets or sets the recency decay half-life in days.
        /// </summary>
        public double RecencyDecayHalfLifeDays { get; set; }

        /// <summary>
        /// Gets or sets the minimum watched items required for personalization.
        /// </summary>
        public int MinWatchedItemsForPersonalization { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to exclude abandoned partially watched series from recommendations.
        /// When enabled, series that haven't been watched within the threshold period are excluded.
        /// </summary>
        public bool ExcludeAbandonedSeries { get; set; }

        /// <summary>
        /// Gets or sets the threshold in days for considering a partially watched series as abandoned.
        /// Series with no watch activity in this period will be excluded from recommendations if ExcludeAbandonedSeries is true.
        /// </summary>
        public int AbandonedSeriesThresholdDays { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for actors (0 = unlimited).
        /// </summary>
        public int MaxVocabularyActors { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for directors (0 = unlimited).
        /// </summary>
        public int MaxVocabularyDirectors { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for tags (0 = unlimited).
        /// </summary>
        public int MaxVocabularyTags { get; set; }

        /// <summary>
        /// Validates the configuration and returns validation errors.
        /// </summary>
        /// <returns>List of validation error messages, empty if valid.</returns>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (MovieRecommendationCount < 0)
            {
                errors.Add("MovieRecommendationCount must be non-negative");
            }

            if (TvRecommendationCount < 0)
            {
                errors.Add("TvRecommendationCount must be non-negative");
            }

            if (FavoriteBoost < 0)
            {
                errors.Add("FavoriteBoost must be non-negative");
            }

            if (RewatchBoost < 0)
            {
                errors.Add("RewatchBoost must be non-negative");
            }

            if (RecencyDecayHalfLifeDays <= 0)
            {
                errors.Add("RecencyDecayHalfLifeDays must be positive");
            }

            if (MinWatchedItemsForPersonalization < 0)
            {
                errors.Add("MinWatchedItemsForPersonalization must be non-negative");
            }

            if (AbandonedSeriesThresholdDays < 1)
            {
                errors.Add("AbandonedSeriesThresholdDays must be at least 1");
            }

            if (MaxVocabularyActors < 0)
            {
                errors.Add("MaxVocabularyActors must be non-negative (0 = unlimited)");
            }

            if (MaxVocabularyDirectors < 0)
            {
                errors.Add("MaxVocabularyDirectors must be non-negative (0 = unlimited)");
            }

            if (MaxVocabularyTags < 0)
            {
                errors.Add("MaxVocabularyTags must be non-negative (0 = unlimited)");
            }

            return errors;
        }
    }
}
