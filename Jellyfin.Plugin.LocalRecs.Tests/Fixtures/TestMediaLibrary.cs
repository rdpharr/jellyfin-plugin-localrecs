using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalRecs.Models;

namespace Jellyfin.Plugin.LocalRecs.Tests.Fixtures
{
    /// <summary>
    /// Provides realistic test data for media items.
    /// </summary>
    public static class TestMediaLibrary
    {
        /// <summary>
        /// Creates a collection of realistic movie metadata for testing.
        /// </summary>
        /// <returns>List of movies with varied characteristics.</returns>
        public static List<MediaItemMetadata> CreateTestMovies()
        {
            var movies = new List<MediaItemMetadata>();

            // Sci-Fi Movies
            var matrix = new MediaItemMetadata(Guid.NewGuid(), "The Matrix", MediaType.Movie)
            {
                ReleaseYear = 1999,
                CommunityRating = 8.7f,
                CriticRating = 88f,
                TmdbId = "603",
                Path = "/media/movies/matrix.mkv"
            };
            matrix.AddGenre("Science Fiction");
            matrix.AddGenre("Action");
            matrix.AddActor("Keanu Reeves");
            matrix.AddActor("Laurence Fishburne");
            matrix.AddActor("Carrie-Anne Moss");
            matrix.AddDirector("Lana Wachowski");
            matrix.AddDirector("Lilly Wachowski");
            matrix.AddTag("Cyberpunk");
            matrix.AddTag("Mind-bending");
            matrix.AddTag("Dystopian");
            movies.Add(matrix);

            var inception = new MediaItemMetadata(Guid.NewGuid(), "Inception", MediaType.Movie)
            {
                ReleaseYear = 2010,
                CommunityRating = 8.8f,
                CriticRating = 87f,
                TmdbId = "27205",
                Path = "/media/movies/inception.mkv"
            };
            inception.AddGenre("Science Fiction");
            inception.AddGenre("Action");
            inception.AddGenre("Thriller");
            inception.AddActor("Leonardo DiCaprio");
            inception.AddActor("Joseph Gordon-Levitt");
            inception.AddActor("Elliot Page");
            inception.AddDirector("Christopher Nolan");
            inception.AddTag("Mind-bending");
            inception.AddTag("Heist");
            inception.AddTag("Dreams");
            movies.Add(inception);

            var bladeRunner = new MediaItemMetadata(Guid.NewGuid(), "Blade Runner 2049", MediaType.Movie)
            {
                ReleaseYear = 2017,
                CommunityRating = 8.0f,
                CriticRating = 88f,
                TmdbId = "335984",
                Path = "/media/movies/bladerunner2049.mkv"
            };
            bladeRunner.AddGenre("Science Fiction");
            bladeRunner.AddGenre("Drama");
            bladeRunner.AddActor("Ryan Gosling");
            bladeRunner.AddActor("Harrison Ford");
            bladeRunner.AddActor("Ana de Armas");
            bladeRunner.AddDirector("Denis Villeneuve");
            bladeRunner.AddTag("Cyberpunk");
            bladeRunner.AddTag("Dystopian");
            bladeRunner.AddTag("Neo-Noir");
            movies.Add(bladeRunner);

            // Drama Movies
            var godfather = new MediaItemMetadata(Guid.NewGuid(), "The Godfather", MediaType.Movie)
            {
                ReleaseYear = 1972,
                CommunityRating = 9.2f,
                CriticRating = 98f,
                TmdbId = "238",
                Path = "/media/movies/godfather.mkv"
            };
            godfather.AddGenre("Drama");
            godfather.AddGenre("Crime");
            godfather.AddActor("Marlon Brando");
            godfather.AddActor("Al Pacino");
            godfather.AddActor("James Caan");
            godfather.AddDirector("Francis Ford Coppola");
            godfather.AddTag("Mafia");
            godfather.AddTag("Classic");
            godfather.AddTag("Epic");
            movies.Add(godfather);

            var shawshank = new MediaItemMetadata(Guid.NewGuid(), "The Shawshank Redemption", MediaType.Movie)
            {
                ReleaseYear = 1994,
                CommunityRating = 9.3f,
                CriticRating = 91f,
                TmdbId = "278",
                Path = "/media/movies/shawshank.mkv"
            };
            shawshank.AddGenre("Drama");
            shawshank.AddActor("Tim Robbins");
            shawshank.AddActor("Morgan Freeman");
            shawshank.AddActor("Bob Gunton");
            shawshank.AddDirector("Frank Darabont");
            shawshank.AddTag("Prison");
            shawshank.AddTag("Classic");
            shawshank.AddTag("Inspirational");
            movies.Add(shawshank);

            // Action/Adventure Movies
            var darkKnight = new MediaItemMetadata(Guid.NewGuid(), "The Dark Knight", MediaType.Movie)
            {
                ReleaseYear = 2008,
                CommunityRating = 9.0f,
                CriticRating = 94f,
                TmdbId = "155",
                Path = "/media/movies/darkknight.mkv"
            };
            darkKnight.AddGenre("Action");
            darkKnight.AddGenre("Crime");
            darkKnight.AddGenre("Drama");
            darkKnight.AddActor("Christian Bale");
            darkKnight.AddActor("Heath Ledger");
            darkKnight.AddActor("Aaron Eckhart");
            darkKnight.AddDirector("Christopher Nolan");
            darkKnight.AddTag("Superhero");
            darkKnight.AddTag("Dark");
            darkKnight.AddTag("Epic");
            movies.Add(darkKnight);

            var interstellar = new MediaItemMetadata(Guid.NewGuid(), "Interstellar", MediaType.Movie)
            {
                ReleaseYear = 2014,
                CommunityRating = 8.6f,
                CriticRating = 72f,
                TmdbId = "157336",
                Path = "/media/movies/interstellar.mkv"
            };
            interstellar.AddGenre("Science Fiction");
            interstellar.AddGenre("Drama");
            interstellar.AddGenre("Adventure");
            interstellar.AddActor("Matthew McConaughey");
            interstellar.AddActor("Anne Hathaway");
            interstellar.AddActor("Jessica Chastain");
            interstellar.AddDirector("Christopher Nolan");
            interstellar.AddTag("Space");
            interstellar.AddTag("Epic");
            interstellar.AddTag("Mind-bending");
            movies.Add(interstellar);

            // Comedy
            var groundhogDay = new MediaItemMetadata(Guid.NewGuid(), "Groundhog Day", MediaType.Movie)
            {
                ReleaseYear = 1993,
                CommunityRating = 8.0f,
                CriticRating = 96f,
                TmdbId = "137",
                Path = "/media/movies/groundhogday.mkv"
            };
            groundhogDay.AddGenre("Comedy");
            groundhogDay.AddGenre("Fantasy");
            groundhogDay.AddGenre("Romance");
            groundhogDay.AddActor("Bill Murray");
            groundhogDay.AddActor("Andie MacDowell");
            groundhogDay.AddActor("Chris Elliott");
            groundhogDay.AddDirector("Harold Ramis");
            groundhogDay.AddTag("Time Loop");
            groundhogDay.AddTag("Classic");
            groundhogDay.AddTag("Feel-good");
            movies.Add(groundhogDay);

            // Horror
            var alien = new MediaItemMetadata(Guid.NewGuid(), "Alien", MediaType.Movie)
            {
                ReleaseYear = 1979,
                CommunityRating = 8.5f,
                CriticRating = 98f,
                TmdbId = "348",
                Path = "/media/movies/alien.mkv"
            };
            alien.AddGenre("Horror");
            alien.AddGenre("Science Fiction");
            alien.AddActor("Sigourney Weaver");
            alien.AddActor("Tom Skerritt");
            alien.AddActor("John Hurt");
            alien.AddDirector("Ridley Scott");
            alien.AddTag("Space");
            alien.AddTag("Survival");
            alien.AddTag("Classic");
            movies.Add(alien);

            // Animation
            var toyStory = new MediaItemMetadata(Guid.NewGuid(), "Toy Story", MediaType.Movie)
            {
                ReleaseYear = 1995,
                CommunityRating = 8.3f,
                CriticRating = 100f,
                TmdbId = "862",
                Path = "/media/movies/toystory.mkv"
            };
            toyStory.AddGenre("Animation");
            toyStory.AddGenre("Comedy");
            toyStory.AddGenre("Family");
            toyStory.AddActor("Tom Hanks");
            toyStory.AddActor("Tim Allen");
            toyStory.AddActor("Don Rickles");
            toyStory.AddDirector("John Lasseter");
            toyStory.AddTag("Pixar");
            toyStory.AddTag("Feel-good");
            toyStory.AddTag("Classic");
            movies.Add(toyStory);

            return movies;
        }

        /// <summary>
        /// Creates a collection of realistic TV series metadata for testing.
        /// </summary>
        /// <returns>List of TV series with varied characteristics.</returns>
        public static List<MediaItemMetadata> CreateTestSeries()
        {
            var series = new List<MediaItemMetadata>();

            // Sci-Fi Series
            var strangerThings = new MediaItemMetadata(Guid.NewGuid(), "Stranger Things", MediaType.Series)
            {
                ReleaseYear = 2016,
                CommunityRating = 8.7f,
                CriticRating = 89f,
                TvdbId = "305288",
                Path = "/media/tv/strangerthings"
            };
            strangerThings.AddGenre("Science Fiction");
            strangerThings.AddGenre("Drama");
            strangerThings.AddGenre("Horror");
            strangerThings.AddActor("Millie Bobby Brown");
            strangerThings.AddActor("Finn Wolfhard");
            strangerThings.AddActor("Winona Ryder");
            strangerThings.AddTag("Supernatural");
            strangerThings.AddTag("80s Nostalgia");
            strangerThings.AddTag("Coming of Age");
            series.Add(strangerThings);

            var westworld = new MediaItemMetadata(Guid.NewGuid(), "Westworld", MediaType.Series)
            {
                ReleaseYear = 2016,
                CommunityRating = 8.6f,
                CriticRating = 73f,
                TvdbId = "296762",
                Path = "/media/tv/westworld"
            };
            westworld.AddGenre("Science Fiction");
            westworld.AddGenre("Western");
            westworld.AddGenre("Drama");
            westworld.AddActor("Evan Rachel Wood");
            westworld.AddActor("Jeffrey Wright");
            westworld.AddActor("Thandiwe Newton");
            westworld.AddTag("AI");
            westworld.AddTag("Dystopian");
            westworld.AddTag("Mind-bending");
            series.Add(westworld);

            // Drama Series
            var breakingBad = new MediaItemMetadata(Guid.NewGuid(), "Breaking Bad", MediaType.Series)
            {
                ReleaseYear = 2008,
                CommunityRating = 9.5f,
                CriticRating = 96f,
                TvdbId = "81189",
                Path = "/media/tv/breakingbad"
            };
            breakingBad.AddGenre("Drama");
            breakingBad.AddGenre("Crime");
            breakingBad.AddGenre("Thriller");
            breakingBad.AddActor("Bryan Cranston");
            breakingBad.AddActor("Aaron Paul");
            breakingBad.AddActor("Anna Gunn");
            breakingBad.AddTag("Dark");
            breakingBad.AddTag("Anti-hero");
            breakingBad.AddTag("Drugs");
            series.Add(breakingBad);

            var theCrown = new MediaItemMetadata(Guid.NewGuid(), "The Crown", MediaType.Series)
            {
                ReleaseYear = 2016,
                CommunityRating = 8.6f,
                CriticRating = 89f,
                TvdbId = "289901",
                Path = "/media/tv/thecrown"
            };
            theCrown.AddGenre("Drama");
            theCrown.AddGenre("History");
            theCrown.AddActor("Claire Foy");
            theCrown.AddActor("Olivia Colman");
            theCrown.AddActor("Imelda Staunton");
            theCrown.AddTag("Royal Family");
            theCrown.AddTag("Biography");
            theCrown.AddTag("British");
            series.Add(theCrown);

            // Comedy Series
            var theOffice = new MediaItemMetadata(Guid.NewGuid(), "The Office", MediaType.Series)
            {
                ReleaseYear = 2005,
                CommunityRating = 9.0f,
                CriticRating = 80f,
                TvdbId = "73244",
                Path = "/media/tv/theoffice"
            };
            theOffice.AddGenre("Comedy");
            theOffice.AddActor("Steve Carell");
            theOffice.AddActor("Rainn Wilson");
            theOffice.AddActor("John Krasinski");
            theOffice.AddTag("Mockumentary");
            theOffice.AddTag("Workplace");
            theOffice.AddTag("Feel-good");
            series.Add(theOffice);

            return series;
        }

        /// <summary>
        /// Creates a mixed collection of movies and TV series.
        /// </summary>
        /// <returns>Combined list of all test media.</returns>
        public static List<MediaItemMetadata> CreateTestLibrary()
        {
            var library = new List<MediaItemMetadata>();
            library.AddRange(CreateTestMovies());
            library.AddRange(CreateTestSeries());
            return library;
        }

        /// <summary>
        /// Creates a minimal test library for quick unit tests.
        /// </summary>
        /// <returns>Small list with 3 items of different genres.</returns>
        public static List<MediaItemMetadata> CreateMinimalLibrary()
        {
            return new List<MediaItemMetadata>
            {
                new MediaItemMetadata(Guid.NewGuid(), "Test Movie 1", MediaType.Movie)
                {
                    ReleaseYear = 2020,
                    CommunityRating = 8.0f
                },
                new MediaItemMetadata(Guid.NewGuid(), "Test Movie 2", MediaType.Movie)
                {
                    ReleaseYear = 2021,
                    CommunityRating = 7.5f
                },
                new MediaItemMetadata(Guid.NewGuid(), "Test Series 1", MediaType.Series)
                {
                    ReleaseYear = 2019,
                    CommunityRating = 8.5f
                }
            };
        }
    }
}
