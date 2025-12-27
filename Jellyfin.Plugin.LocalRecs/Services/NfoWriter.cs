using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Generates NFO metadata files for virtual library items.
    /// NFO files allow Jellyfin to read metadata like runtime, trailers, and other details
    /// for .strm files that don't have embedded metadata.
    /// </summary>
    public class NfoWriter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NfoWriter"/> class.
        /// </summary>
        public NfoWriter()
        {
        }

        /// <summary>
        /// Generates NFO content for a movie.
        /// </summary>
        /// <param name="item">The movie item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateMovieNfo(BaseItem item)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("movie");

                WriteCommonElements(writer, item);
                WriteMovieSpecificElements(writer, item);

                writer.WriteEndElement(); // movie
                writer.WriteEndDocument();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates NFO content for a TV series.
        /// </summary>
        /// <param name="series">The series item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateSeriesNfo(Series series)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("tvshow");

                WriteCommonElements(writer, series);
                WriteSeriesSpecificElements(writer, series);

                writer.WriteEndElement(); // tvshow
                writer.WriteEndDocument();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates NFO content for a TV episode.
        /// </summary>
        /// <param name="episode">The episode item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateEpisodeNfo(Episode episode)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("episodedetails");

                WriteCommonElements(writer, episode);
                WriteEpisodeSpecificElements(writer, episode);

                writer.WriteEndElement(); // episodedetails
                writer.WriteEndDocument();
            }

            return sb.ToString();
        }

        private void WriteCommonElements(XmlWriter writer, BaseItem item)
        {
            // Title
            if (!string.IsNullOrEmpty(item.Name))
            {
                writer.WriteElementString("title", item.Name);
            }

            // Original title
            if (!string.IsNullOrEmpty(item.OriginalTitle))
            {
                writer.WriteElementString("originaltitle", item.OriginalTitle);
            }

            // Sort title
            if (!string.IsNullOrEmpty(item.ForcedSortName))
            {
                writer.WriteElementString("sorttitle", item.ForcedSortName);
            }

            // Plot/Overview
            if (!string.IsNullOrEmpty(item.Overview))
            {
                writer.WriteElementString("plot", item.Overview);
            }

            // Tagline
            if (!string.IsNullOrEmpty(item.Tagline))
            {
                writer.WriteElementString("tagline", item.Tagline);
            }

            // Runtime (in minutes)
            if (item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0)
            {
                var runtimeMinutes = (int)(item.RunTimeTicks.Value / TimeSpan.TicksPerMinute);
                writer.WriteElementString("runtime", runtimeMinutes.ToString(CultureInfo.InvariantCulture));
            }

            // Year
            if (item.ProductionYear.HasValue)
            {
                writer.WriteElementString("year", item.ProductionYear.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Premiere date
            if (item.PremiereDate.HasValue)
            {
                writer.WriteElementString("premiered", item.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            // MPAA rating
            if (!string.IsNullOrEmpty(item.OfficialRating))
            {
                writer.WriteElementString("mpaa", item.OfficialRating);
            }

            // Community rating
            if (item.CommunityRating.HasValue)
            {
                writer.WriteElementString("rating", item.CommunityRating.Value.ToString("F1", CultureInfo.InvariantCulture));
            }

            // Critic rating
            if (item.CriticRating.HasValue)
            {
                writer.WriteElementString("criticrating", item.CriticRating.Value.ToString("F0", CultureInfo.InvariantCulture));
            }

            // Genres
            foreach (var genre in item.Genres ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(genre))
                {
                    writer.WriteElementString("genre", genre);
                }
            }

            // Studios
            foreach (var studio in item.Studios ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(studio))
                {
                    writer.WriteElementString("studio", studio);
                }
            }

            // Tags
            foreach (var tag in item.Tags ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    writer.WriteElementString("tag", tag);
                }
            }

            // Provider IDs
            WriteProviderIds(writer, item);
        }

        private void WriteMovieSpecificElements(XmlWriter writer, BaseItem item)
        {
            // Collection/Set info for movies
            if (item is Movie movie)
            {
                if (!string.IsNullOrEmpty(movie.CollectionName))
                {
                    writer.WriteElementString("set", movie.CollectionName);
                }
            }
        }

        private void WriteSeriesSpecificElements(XmlWriter writer, Series series)
        {
            // Status (Continuing, Ended, etc.)
            if (series.Status.HasValue)
            {
                writer.WriteElementString("status", series.Status.Value.ToString());
            }
        }

        private void WriteEpisodeSpecificElements(XmlWriter writer, Episode episode)
        {
            // Season number
            if (episode.ParentIndexNumber.HasValue)
            {
                writer.WriteElementString("season", episode.ParentIndexNumber.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Episode number
            if (episode.IndexNumber.HasValue)
            {
                writer.WriteElementString("episode", episode.IndexNumber.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Aired date
            if (episode.PremiereDate.HasValue)
            {
                writer.WriteElementString("aired", episode.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            // Show title
            if (!string.IsNullOrEmpty(episode.SeriesName))
            {
                writer.WriteElementString("showtitle", episode.SeriesName);
            }
        }

        private void WriteProviderIds(XmlWriter writer, BaseItem item)
        {
            var providerIds = item.ProviderIds ?? new Dictionary<string, string>();

            // IMDB
            if (providerIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId))
            {
                writer.WriteElementString("imdbid", imdbId);
            }

            // TMDB
            if (providerIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId))
            {
                writer.WriteElementString("tmdbid", tmdbId);
            }

            // TVDB
            if (providerIds.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId))
            {
                writer.WriteElementString("tvdbid", tvdbId);
            }
        }
    }
}
