using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Generates NFO metadata files for virtual library items.
    /// NFO files allow Jellyfin to read metadata like runtime, trailers, and other details
    /// for .strm files that don't have embedded metadata.
    /// </summary>
    public class NfoWriter
    {
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="NfoWriter"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager for accessing people data.</param>
        public NfoWriter(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        }

        /// <summary>
        /// Generates NFO content for a movie.
        /// </summary>
        /// <param name="item">The movie item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateMovieNfo(BaseItem item)
        {
            using var memoryStream = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                CloseOutput = false
            };

            using (var writer = XmlWriter.Create(memoryStream, settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("movie");

                WriteCommonElements(writer, item);
                WriteMovieSpecificElements(writer, item);
                WritePeople(writer, item);
                WriteMediaInfo(writer, item);

                writer.WriteEndElement(); // movie
                writer.WriteEndDocument();
            }

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Generates NFO content for a TV series.
        /// </summary>
        /// <param name="series">The series item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateSeriesNfo(Series series)
        {
            using var memoryStream = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                CloseOutput = false
            };

            using (var writer = XmlWriter.Create(memoryStream, settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("tvshow");

                WriteCommonElements(writer, series);
                WriteSeriesSpecificElements(writer, series);
                WritePeople(writer, series);

                writer.WriteEndElement(); // tvshow
                writer.WriteEndDocument();
            }

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Generates NFO content for a TV episode.
        /// </summary>
        /// <param name="episode">The episode item.</param>
        /// <returns>NFO XML content as a string.</returns>
        public string GenerateEpisodeNfo(Episode episode)
        {
            using var memoryStream = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                CloseOutput = false
            };

            using (var writer = XmlWriter.Create(memoryStream, settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("episodedetails");

                WriteCommonElements(writer, episode);
                WriteEpisodeSpecificElements(writer, episode);
                WritePeople(writer, episode);
                WriteMediaInfo(writer, episode);

                writer.WriteEndElement(); // episodedetails
                writer.WriteEndDocument();
            }

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private void WriteCommonElements(XmlWriter writer, BaseItem item)
        {
            // Plot/Overview (write first like Jellyfin does)
            if (!string.IsNullOrEmpty(item.Overview))
            {
                writer.WriteElementString("plot", item.Overview);
            }

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

            // Tagline
            if (!string.IsNullOrEmpty(item.Tagline))
            {
                writer.WriteElementString("tagline", item.Tagline);
            }

            // Community rating
            if (item.CommunityRating.HasValue)
            {
                writer.WriteElementString("rating", item.CommunityRating.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Year
            if (item.ProductionYear.HasValue)
            {
                writer.WriteElementString("year", item.ProductionYear.Value.ToString(CultureInfo.InvariantCulture));
            }

            // MPAA rating
            if (!string.IsNullOrEmpty(item.OfficialRating))
            {
                writer.WriteElementString("mpaa", item.OfficialRating);
            }

            // Critic rating
            if (item.CriticRating.HasValue)
            {
                writer.WriteElementString("criticrating", item.CriticRating.Value.ToString(CultureInfo.InvariantCulture));
            }

            // Runtime (in minutes) - use Convert.ToInt64 like Jellyfin does
            if (item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0)
            {
                var timespan = TimeSpan.FromTicks(item.RunTimeTicks.Value);
                writer.WriteElementString("runtime", Convert.ToInt64(timespan.TotalMinutes).ToString(CultureInfo.InvariantCulture));
            }

            // Premiere date
            if (item.PremiereDate.HasValue)
            {
                writer.WriteElementString("premiered", item.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteElementString("releasedate", item.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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

        private void WritePeople(XmlWriter writer, BaseItem item)
        {
            var people = _libraryManager.GetPeople(item);
            if (people == null || people.Count == 0)
            {
                return;
            }

            // Directors
            var directors = people
                .Where(p => p.Type == PersonKind.Director)
                .Select(p => p.Name?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n);

            foreach (var director in directors)
            {
                writer.WriteElementString("director", director);
            }

            // Writers (both as writer and credits tags)
            var writers = people
                .Where(p => p.Type == PersonKind.Writer)
                .Select(p => p.Name?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            foreach (var writerName in writers)
            {
                writer.WriteElementString("writer", writerName);
            }

            foreach (var writerName in writers)
            {
                writer.WriteElementString("credits", writerName);
            }

            // Actors
            var actors = people
                .Where(p => p.Type != PersonKind.Director && p.Type != PersonKind.Writer)
                .OrderBy(p => p.SortOrder ?? int.MaxValue)
                .ThenBy(p => p.Name?.Trim());

            foreach (var person in actors)
            {
                if (string.IsNullOrWhiteSpace(person.Name))
                {
                    continue;
                }

                writer.WriteStartElement("actor");
                writer.WriteElementString("name", person.Name.Trim());

                if (!string.IsNullOrWhiteSpace(person.Role))
                {
                    writer.WriteElementString("role", person.Role);
                }

                if (person.Type != PersonKind.Unknown)
                {
                    writer.WriteElementString("type", person.Type.ToString());
                }

                if (person.SortOrder.HasValue)
                {
                    writer.WriteElementString("sortorder", person.SortOrder.Value.ToString(CultureInfo.InvariantCulture));
                }

                // Try to get actor thumbnail
                var personEntity = _libraryManager.GetPerson(person.Name);
                if (personEntity != null)
                {
                    var image = personEntity.GetImageInfo(ImageType.Primary, 0);
                    if (image != null && !string.IsNullOrEmpty(image.Path))
                    {
                        writer.WriteElementString("thumb", image.Path);
                    }
                }

                writer.WriteEndElement(); // actor
            }
        }

        private void WriteMediaInfo(XmlWriter writer, BaseItem item)
        {
            if (item is not IHasMediaSources hasMediaSources)
            {
                return;
            }

            IReadOnlyList<MediaStream>? mediaStreams;
            try
            {
                mediaStreams = hasMediaSources.GetMediaStreams();
            }
            catch
            {
                // GetMediaStreams can throw if media sources aren't available
                return;
            }

            if (mediaStreams == null || mediaStreams.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("fileinfo");
            writer.WriteStartElement("streamdetails");

            foreach (var stream in mediaStreams)
            {
                writer.WriteStartElement(stream.Type.ToString().ToLowerInvariant());

                if (!string.IsNullOrEmpty(stream.Codec))
                {
                    var codec = stream.Codec;

                    // Normalize codec names like Jellyfin does
                    if ((stream.CodecTag ?? string.Empty).Contains("xvid", StringComparison.OrdinalIgnoreCase))
                    {
                        codec = "xvid";
                    }
                    else if ((stream.CodecTag ?? string.Empty).Contains("divx", StringComparison.OrdinalIgnoreCase))
                    {
                        codec = "divx";
                    }

                    writer.WriteElementString("codec", codec);
                    writer.WriteElementString("micodec", codec);
                }

                if (stream.BitRate.HasValue)
                {
                    writer.WriteElementString("bitrate", stream.BitRate.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (stream.Width.HasValue)
                {
                    writer.WriteElementString("width", stream.Width.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (stream.Height.HasValue)
                {
                    writer.WriteElementString("height", stream.Height.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (!string.IsNullOrEmpty(stream.AspectRatio))
                {
                    writer.WriteElementString("aspect", stream.AspectRatio);
                    writer.WriteElementString("aspectratio", stream.AspectRatio);
                }

                if (stream.ReferenceFrameRate.HasValue)
                {
                    writer.WriteElementString("framerate", stream.ReferenceFrameRate.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (!string.IsNullOrEmpty(stream.Language))
                {
                    writer.WriteElementString("language", stream.Language);
                }

                if (stream.Type == MediaStreamType.Video)
                {
                    var scanType = stream.IsInterlaced ? "interlaced" : "progressive";
                    writer.WriteElementString("scantype", scanType);

                    // Duration info
                    if (item.RunTimeTicks.HasValue)
                    {
                        var timespan = TimeSpan.FromTicks(item.RunTimeTicks.Value);
                        writer.WriteElementString("duration", Math.Floor(timespan.TotalMinutes).ToString(CultureInfo.InvariantCulture));
                        writer.WriteElementString("durationinseconds", Math.Floor(timespan.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (stream.Channels.HasValue)
                {
                    writer.WriteElementString("channels", stream.Channels.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (stream.SampleRate.HasValue)
                {
                    writer.WriteElementString("samplingrate", stream.SampleRate.Value.ToString(CultureInfo.InvariantCulture));
                }

                writer.WriteElementString("default", stream.IsDefault.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString("forced", stream.IsForced.ToString(CultureInfo.InvariantCulture));

                writer.WriteEndElement(); // video/audio/subtitle
            }

            writer.WriteEndElement(); // streamdetails
            writer.WriteEndElement(); // fileinfo
        }

        private void WriteMovieSpecificElements(XmlWriter writer, BaseItem item)
        {
            // Collection/Set info for movies
            if (item is Movie movie)
            {
                if (!string.IsNullOrEmpty(movie.CollectionName))
                {
                    writer.WriteStartElement("set");
                    writer.WriteElementString("name", movie.CollectionName);
                    writer.WriteEndElement();
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
