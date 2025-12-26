using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalRecs.Models;

namespace Jellyfin.Plugin.LocalRecs.Tests.Fixtures
{
    /// <summary>
    /// Provides test data for user watch history.
    /// </summary>
    public static class TestUserData
    {
        /// <summary>
        /// Creates a collection of watch records simulating a sci-fi fan.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library (to get item IDs).</param>
        /// <returns>Watch records for a sci-fi enthusiast.</returns>
        public static List<WatchRecord> CreateSciFiFan(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // The Matrix - watched recently, marked as favorite
            var matrix = library.Find(i => i.Name == "The Matrix");
            if (matrix != null)
            {
                records.Add(new WatchRecord(matrix.Id, userId, now.AddDays(-7))
                {
                    IsFavorite = true,
                    PlayCount = 3
                });
            }

            // Inception - watched recently
            var inception = library.Find(i => i.Name == "Inception");
            if (inception != null)
            {
                records.Add(new WatchRecord(inception.Id, userId, now.AddDays(-14))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            // Blade Runner 2049 - watched recently
            var bladeRunner = library.Find(i => i.Name == "Blade Runner 2049");
            if (bladeRunner != null)
            {
                records.Add(new WatchRecord(bladeRunner.Id, userId, now.AddDays(-21))
                {
                    IsFavorite = true,
                    PlayCount = 2
                });
            }

            // Interstellar - watched a while ago
            var interstellar = library.Find(i => i.Name == "Interstellar");
            if (interstellar != null)
            {
                records.Add(new WatchRecord(interstellar.Id, userId, now.AddDays(-90))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            // Alien - watched long ago
            var alien = library.Find(i => i.Name == "Alien");
            if (alien != null)
            {
                records.Add(new WatchRecord(alien.Id, userId, now.AddDays(-180))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records simulating a drama fan.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records for a drama enthusiast.</returns>
        public static List<WatchRecord> CreateDramaFan(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // The Godfather - favorite, rewatched multiple times
            var godfather = library.Find(i => i.Name == "The Godfather");
            if (godfather != null)
            {
                records.Add(new WatchRecord(godfather.Id, userId, now.AddDays(-10))
                {
                    IsFavorite = true,
                    PlayCount = 5
                });
            }

            // The Shawshank Redemption - favorite
            var shawshank = library.Find(i => i.Name == "The Shawshank Redemption");
            if (shawshank != null)
            {
                records.Add(new WatchRecord(shawshank.Id, userId, now.AddDays(-20))
                {
                    IsFavorite = true,
                    PlayCount = 3
                });
            }

            // The Dark Knight - watched once
            var darkKnight = library.Find(i => i.Name == "The Dark Knight");
            if (darkKnight != null)
            {
                records.Add(new WatchRecord(darkKnight.Id, userId, now.AddDays(-30))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records for a user with minimal history (cold start scenario).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records with only 2 items.</returns>
        public static List<WatchRecord> CreateColdStartUser(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // Only watched 2 movies
            var toyStory = library.Find(i => i.Name == "Toy Story");
            if (toyStory != null)
            {
                records.Add(new WatchRecord(toyStory.Id, userId, now.AddDays(-5))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            var groundhogDay = library.Find(i => i.Name == "Groundhog Day");
            if (groundhogDay != null)
            {
                records.Add(new WatchRecord(groundhogDay.Id, userId, now.AddDays(-10))
                {
                    IsFavorite = false,
                    PlayCount = 1
                });
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records for a user who loves Christopher Nolan films.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records focused on Nolan films.</returns>
        public static List<WatchRecord> CreateNolanFan(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // All Nolan films in library marked as favorites
            var nolanFilms = new[] { "Inception", "The Dark Knight", "Interstellar" };

            for (int i = 0; i < nolanFilms.Length; i++)
            {
                var film = library.Find(item => item.Name == nolanFilms[i]);
                if (film != null)
                {
                    records.Add(new WatchRecord(film.Id, userId, now.AddDays(-5 - (i * 8)))
                    {
                        IsFavorite = true,
                        PlayCount = 2 + (i % 3)
                    });
                }
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records with heavy recency bias (all recent watches).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records all within the last week.</returns>
        public static List<WatchRecord> CreateRecentWatcher(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // Watched 5 random movies in the last week
            var candidates = new[] { "The Matrix", "Inception", "Toy Story", "The Godfather", "Alien" };

            for (int i = 0; i < 5; i++)
            {
                var film = library.Find(item => item.Name == candidates[i]);
                if (film != null)
                {
                    records.Add(new WatchRecord(film.Id, userId, now.AddDays(-i))
                    {
                        IsFavorite = false,
                        PlayCount = 1
                    });
                }
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records with old watches (recency decay testing).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records all from over a year ago.</returns>
        public static List<WatchRecord> CreateOldWatcher(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // Watched movies 1-2 years ago
            var candidates = new[] { "The Godfather", "The Shawshank Redemption", "Alien" };

            for (int i = 0; i < 3; i++)
            {
                var film = library.Find(item => item.Name == candidates[i]);
                if (film != null)
                {
                    records.Add(new WatchRecord(film.Id, userId, now.AddDays(-365 - (i * 30)))
                    {
                        IsFavorite = false,
                        PlayCount = 1
                    });
                }
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records for a TV series watcher.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records focused on TV series.</returns>
        public static List<WatchRecord> CreateSeriesWatcher(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // Focus on TV series
            var seriesNames = new[] { "Breaking Bad", "Stranger Things", "The Office" };

            for (int i = 0; i < seriesNames.Length; i++)
            {
                var series = library.Find(item => item.Name == seriesNames[i]);
                if (series != null)
                {
                    records.Add(new WatchRecord(series.Id, userId, now.AddDays(-10 - (i * 15)))
                    {
                        IsFavorite = seriesNames[i] == "Breaking Bad",
                        PlayCount = 1 + (i % 2)
                    });
                }
            }

            return records;
        }

        /// <summary>
        /// Creates a collection of watch records with diverse preferences (mixed genres).
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="library">The media library.</param>
        /// <returns>Watch records across multiple genres.</returns>
        public static List<WatchRecord> CreateDiverseWatcher(Guid userId, List<MediaItemMetadata> library)
        {
            var records = new List<WatchRecord>();
            var now = DateTime.UtcNow;

            // One from each major genre
            var diverseFilms = new[]
            {
                "The Matrix",          // Sci-Fi
                "The Godfather",       // Drama/Crime
                "Groundhog Day",       // Comedy
                "Alien",               // Horror/Sci-Fi
                "Toy Story"            // Animation/Family
            };

            for (int i = 0; i < diverseFilms.Length; i++)
            {
                var film = library.Find(item => item.Name == diverseFilms[i]);
                if (film != null)
                {
                    records.Add(new WatchRecord(film.Id, userId, now.AddDays(-15 - (i * 5)))
                    {
                        IsFavorite = i == 0, // Only first one is favorite
                        PlayCount = 1
                    });
                }
            }

            return records;
        }

        /// <summary>
        /// Creates watch history for a sci-fi fan in tuple format for integration tests.
        /// </summary>
        /// <param name="library">The media library.</param>
        /// <returns>Tuples of (Item, IsFavorite, PlayCount, DaysAgo) for mocking.</returns>
        public static List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)> CreateSciFiFanHistory(
            List<MediaItemMetadata> library)
        {
            var history = new List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)>();

            // The Matrix - watched recently, marked as favorite
            var matrix = library.Find(i => i.Name == "The Matrix");
            if (matrix != null)
            {
                history.Add((matrix, true, 3, 7));
            }

            // Inception - watched recently
            var inception = library.Find(i => i.Name == "Inception");
            if (inception != null)
            {
                history.Add((inception, false, 1, 14));
            }

            // Blade Runner 2049 - watched recently
            var bladeRunner = library.Find(i => i.Name == "Blade Runner 2049");
            if (bladeRunner != null)
            {
                history.Add((bladeRunner, true, 2, 21));
            }

            // Interstellar - watched a while ago
            var interstellar = library.Find(i => i.Name == "Interstellar");
            if (interstellar != null)
            {
                history.Add((interstellar, false, 1, 90));
            }

            // Alien - watched long ago
            var alien = library.Find(i => i.Name == "Alien");
            if (alien != null)
            {
                history.Add((alien, false, 1, 180));
            }

            return history;
        }

        /// <summary>
        /// Creates watch history for a drama fan in tuple format for integration tests.
        /// </summary>
        /// <param name="library">The media library.</param>
        /// <returns>Tuples of (Item, IsFavorite, PlayCount, DaysAgo) for mocking.</returns>
        public static List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)> CreateDramaFanHistory(
            List<MediaItemMetadata> library)
        {
            var history = new List<(MediaItemMetadata Item, bool IsFavorite, int PlayCount, int DaysAgo)>();

            // The Godfather - favorite, rewatched multiple times
            var godfather = library.Find(i => i.Name == "The Godfather");
            if (godfather != null)
            {
                history.Add((godfather, true, 5, 10));
            }

            // The Shawshank Redemption - favorite
            var shawshank = library.Find(i => i.Name == "The Shawshank Redemption");
            if (shawshank != null)
            {
                history.Add((shawshank, true, 3, 20));
            }

            // The Dark Knight - watched once
            var darkKnight = library.Find(i => i.Name == "The Dark Knight");
            if (darkKnight != null)
            {
                history.Add((darkKnight, false, 1, 30));
            }

            // Breaking Bad series - favorite
            var breakingBad = library.Find(i => i.Name == "Breaking Bad");
            if (breakingBad != null)
            {
                history.Add((breakingBad, true, 2, 60));
            }

            return history;
        }
    }
}
