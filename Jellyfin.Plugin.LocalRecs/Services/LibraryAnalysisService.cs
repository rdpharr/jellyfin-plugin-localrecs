using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LocalRecs.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Service for querying and analyzing the Jellyfin library.
    /// Converts Jellyfin BaseItem objects to our MediaItemMetadata abstraction layer.
    /// </summary>
    public class LibraryAnalysisService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<LibraryAnalysisService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryAnalysisService"/> class.
        /// </summary>
        /// <param name="libraryManager">The Jellyfin library manager.</param>
        /// <param name="logger">The logger.</param>
        public LibraryAnalysisService(
            ILibraryManager libraryManager,
            ILogger<LibraryAnalysisService> logger)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets all movies and TV series from the library as MediaItemMetadata objects.
        /// </summary>
        /// <returns>List of media item metadata.</returns>
        public IReadOnlyList<MediaItemMetadata> GetAllMediaItems()
        {
            try
            {
                var items = new List<MediaItemMetadata>();

                // Get all movies
                var movies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true
                });

                foreach (var movie in movies.OfType<Movie>())
                {
                    var metadata = ConvertToMetadata(movie, Models.MediaType.Movie);
                    if (metadata != null)
                    {
                        items.Add(metadata);
                    }
                }

                // Get all TV series
                var series = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    IsVirtualItem = false,
                    Recursive = true
                });

                foreach (var show in series.OfType<Series>())
                {
                    var metadata = ConvertToMetadata(show, Models.MediaType.Series);
                    if (metadata != null)
                    {
                        items.Add(metadata);
                    }
                }

                _logger.LogInformation(
                    "Retrieved {Count} media items from library ({Movies} movies, {Series} series)",
                    items.Count,
                    movies.Count,
                    series.Count);

                return items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving media items from library");
                throw;
            }
        }

        /// <summary>
        /// Gets a single media item by its ID.
        /// </summary>
        /// <param name="itemId">The item ID.</param>
        /// <returns>MediaItemMetadata or null if not found.</returns>
        public MediaItemMetadata? GetMediaItem(Guid itemId)
        {
            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    return null;
                }

                return item switch
                {
                    Movie movie => ConvertToMetadata(movie, Models.MediaType.Movie),
                    Series series => ConvertToMetadata(series, Models.MediaType.Series),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving media item {ItemId}", itemId);
                throw;
            }
        }

        /// <summary>
        /// Converts a Jellyfin BaseItem to our MediaItemMetadata abstraction.
        /// </summary>
        /// <param name="item">The Jellyfin item.</param>
        /// <param name="mediaType">The media type.</param>
        /// <returns>MediaItemMetadata or null if conversion fails.</returns>
        private MediaItemMetadata? ConvertToMetadata(BaseItem item, Models.MediaType mediaType)
        {
            if (item == null || string.IsNullOrEmpty(item.Name))
            {
                return null;
            }

            // Skip items from virtual libraries (our .strm recommendation files)
            // This prevents virtual library items from being considered for recommendations
            if (!string.IsNullOrEmpty(item.Path) &&
                (item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) ||
                 item.Path.Contains("virtual-libraries", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug(
                    "Skipping virtual library item: {ItemName} ({ItemId}) at {Path}",
                    item.Name,
                    item.Id,
                    item.Path);
                return null;
            }

            var metadata = new MediaItemMetadata(item.Id, item.Name, mediaType);

            // Add genres
            if (item.Genres != null)
            {
                foreach (var genre in item.Genres)
                {
                    if (!string.IsNullOrWhiteSpace(genre))
                    {
                        metadata.AddGenre(genre);
                    }
                }
            }

            // Add actors and directors from item metadata
            // Get all people for this item in a single call
            var people = _libraryManager.GetPeople(new InternalPeopleQuery
            {
                ItemId = item.Id
            });

            // Filter actors (vocabulary limiting happens in VocabularyBuilder)
            var actors = people
                .Where(p => p.Type == PersonKind.Actor)
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name));

            foreach (var actor in actors)
            {
                metadata.AddActor(actor);
            }

            // Filter directors
            var directors = people
                .Where(p => p.Type == PersonKind.Director)
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name));

            foreach (var director in directors)
            {
                metadata.AddDirector(director);
            }

            // Add tags
            if (item.Tags != null)
            {
                foreach (var tag in item.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        metadata.AddTag(tag);
                    }
                }
            }

            // Set ratings
            metadata.CommunityRating = item.CommunityRating;
            metadata.CriticRating = item.CriticRating;

            // Set release year
            if (item.ProductionYear.HasValue)
            {
                metadata.ReleaseYear = item.ProductionYear.Value;
            }

            // Set provider IDs using GetProviderId extension method
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            if (!string.IsNullOrEmpty(tmdbId))
            {
                metadata.TmdbId = tmdbId;
            }

            var tvdbId = item.GetProviderId(MetadataProvider.Tvdb);
            if (!string.IsNullOrEmpty(tvdbId))
            {
                metadata.TvdbId = tvdbId;
            }

            // Set file path
            metadata.Path = item.Path;

            return metadata;
        }
    }
}
