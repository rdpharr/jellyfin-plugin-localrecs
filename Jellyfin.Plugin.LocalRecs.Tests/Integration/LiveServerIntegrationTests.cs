using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Models;
using Jellyfin.Plugin.LocalRecs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.LocalRecs.Tests.Integration
{
    /// <summary>
    /// Live integration tests that run against a real Jellyfin server.
    /// These tests validate the full recommendation pipeline with real library data.
    ///
    /// Prerequisites:
    /// - Jellyfin server running at the configured address
    /// - Valid API key with read access
    /// - At least one user with watch history
    ///
    /// These tests are skipped if the server is not available.
    /// </summary>
    [Trait("Category", "LiveIntegration")]
    public class LiveServerIntegrationTests : IAsyncLifetime
    {
        /// <summary>
        /// Jellyfin server URL. Set via JELLYFIN_TEST_SERVER environment variable.
        /// Defaults to localhost:8096 if not set.
        /// </summary>
        private static readonly string ServerUrl =
            Environment.GetEnvironmentVariable("JELLYFIN_TEST_SERVER") ?? "http://localhost:8096";

        /// <summary>
        /// Jellyfin API key for test access. Set via JELLYFIN_TEST_API_KEY environment variable.
        /// Tests will be skipped if not set.
        /// </summary>
        private static readonly string? ApiKey =
            Environment.GetEnvironmentVariable("JELLYFIN_TEST_API_KEY");

        /// <summary>
        /// Threshold for considering a series as "watched" based on played percentage.
        /// </summary>
        private const double WatchedPercentageThreshold = 80.0;

        private readonly HttpClient _httpClient;
        private readonly ITestOutputHelper _output;
        private bool _serverAvailable;

        public LiveServerIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ServerUrl),
                Timeout = TimeSpan.FromSeconds(120) // Increased for large library fetches
            };

            if (!string.IsNullOrEmpty(ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", ApiKey);
            }
        }

        public async Task InitializeAsync()
        {
            // Check if API key is configured
            if (string.IsNullOrEmpty(ApiKey))
            {
                _output.WriteLine("JELLYFIN_TEST_API_KEY environment variable not set. Tests will be skipped.");
                _output.WriteLine("To run live integration tests, set:");
                _output.WriteLine("  JELLYFIN_TEST_SERVER=http://your-server:8096");
                _output.WriteLine("  JELLYFIN_TEST_API_KEY=your-api-key");
                _serverAvailable = false;
                return;
            }

            try
            {
                var response = await _httpClient.GetAsync("/System/Info/Public");
                _serverAvailable = response.IsSuccessStatusCode;
                if (_serverAvailable)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _output.WriteLine($"Connected to Jellyfin server: {content}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Server not available: {ex.Message}");
                _serverAvailable = false;
            }
        }

        public Task DisposeAsync()
        {
            _httpClient.Dispose();
            return Task.CompletedTask;
        }

        [SkippableFact]
        public async Task LivePipeline_WithRealLibrary_ProducesValidRecommendations()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            // Step 1: Fetch library metadata
            _output.WriteLine("Fetching library...");
            var library = await FetchLibraryAsync();
            _output.WriteLine($"Fetched {library.Count} items ({library.Count(i => i.Type == MediaType.Movie)} movies, {library.Count(i => i.Type == MediaType.Series)} series)");

            library.Should().NotBeEmpty("library should have content");

            // Debug: Check for tags in library items
            var itemsWithTags = library.Where(i => i.Tags.Count > 0).ToList();
            _output.WriteLine($"Items with tags: {itemsWithTags.Count} out of {library.Count}");
            if (itemsWithTags.Count > 0)
            {
                var sampleItem = itemsWithTags.First();
                _output.WriteLine($"Sample item with tags: {sampleItem.Name} has {sampleItem.Tags.Count} tags: {string.Join(", ", sampleItem.Tags.Take(5))}");
            }

            // Step 2: Build vocabulary
            _output.WriteLine("Building vocabulary...");
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library, maxActors: 500, maxDirectors: 0, maxTags: 500);

            _output.WriteLine($"Vocabulary: {vocabulary.Genres.Count} genres, {vocabulary.Actors.Count} actors, {vocabulary.Directors.Count} directors, {vocabulary.Tags.Count} tags");
            _output.WriteLine($"Top genres: {string.Join(", ", vocabulary.Genres.OrderByDescending(g => g.Value).Take(10).Select(g => g.Key))}");
            if (vocabulary.Tags.Count > 0)
            {
                _output.WriteLine($"Top tags: {string.Join(", ", vocabulary.Tags.OrderByDescending(t => t.Value).Take(10).Select(t => t.Key))}");
            }

            vocabulary.Genres.Should().NotBeEmpty();
            vocabulary.Actors.Should().NotBeEmpty();

            // Step 3: Compute embeddings
            _output.WriteLine("Computing embeddings...");
            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);

            _output.WriteLine($"Computed {embeddings.Count} embeddings, dimension: {embeddings.Values.First().Dimensions}");

            embeddings.Count.Should().Be(library.Count);
            embeddings.Values.Should().AllSatisfy(e => e.Dimensions.Should().BeGreaterThan(0));

            // Step 4: Fetch user watch history
            _output.WriteLine("Fetching watch history...");
            var (watchedItems, userProfile) = await FetchUserWatchHistoryAsync(library, embeddings);

            _output.WriteLine($"User has watched {watchedItems.Count} items");
            if (watchedItems.Any())
            {
                _output.WriteLine($"Sample watched: {string.Join(", ", watchedItems.Take(5).Select(w => w.Name))}");
            }

            // Step 5: Generate recommendations (manually, since we can't use the full service without Jellyfin DI)
            _output.WriteLine("Generating recommendations...");
            
            // Generate movie recommendations (25)
            var movieRecommendations = GenerateRecommendations(library, embeddings, watchedItems, userProfile, MediaType.Movie, 25);
            
            // Generate TV recommendations (25)
            var tvRecommendations = GenerateRecommendations(library, embeddings, watchedItems, userProfile, MediaType.Series, 25);

            _output.WriteLine($"Generated {movieRecommendations.Count} movie recommendations, {tvRecommendations.Count} TV recommendations");

            // Step 6: Validate recommendations
            movieRecommendations.Should().NotBeEmpty("should produce movie recommendations for user with watch history");
            tvRecommendations.Should().NotBeEmpty("should produce TV recommendations for user with watch history");

            // Output top movie recommendations
            _output.WriteLine("\n=== TOP 25 MOVIE RECOMMENDATIONS ===");
            var metadata = library.ToDictionary(i => i.Id);
            foreach (var rec in movieRecommendations)
            {
                var item = metadata[rec.ItemId];
                _output.WriteLine($"  {rec.Score:F4} - {item.Name} ({item.ReleaseYear}) [{string.Join(", ", item.Genres)}]");
            }

            // Output top TV recommendations
            _output.WriteLine("\n=== TOP 25 TV RECOMMENDATIONS ===");
            foreach (var rec in tvRecommendations)
            {
                var item = metadata[rec.ItemId];
                _output.WriteLine($"  {rec.Score:F4} - {item.Name} ({item.ReleaseYear}) [{string.Join(", ", item.Genres)}]");
            }

            // Validate movie scores are in valid range
            movieRecommendations.Should().AllSatisfy(r =>
            {
                r.Score.Should().BeInRange(0, 1, "scores should be normalized");
                double.IsNaN(r.Score).Should().BeFalse("scores should not be NaN");
            });

            // Validate TV scores are in valid range
            tvRecommendations.Should().AllSatisfy(r =>
            {
                r.Score.Should().BeInRange(0, 1, "scores should be normalized");
                double.IsNaN(r.Score).Should().BeFalse("scores should not be NaN");
            });

            // Recommendations should not include watched items
            var watchedIds = watchedItems.Select(w => w.Id).ToHashSet();
            movieRecommendations.Should().NotContain(r => watchedIds.Contains(r.ItemId),
                "movie recommendations should not include watched items");
            tvRecommendations.Should().NotContain(r => watchedIds.Contains(r.ItemId),
                "TV recommendations should not include watched items");
        }

        [SkippableFact]
        public async Task LivePipeline_AnalyzeUserTaste_ShowsGenrePreferences()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            var library = await FetchLibraryAsync();
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);

            var (watchedItems, _) = await FetchUserWatchHistoryAsync(library, embeddings);

            // Analyze genre preferences from watch history
            var genreCounts = new Dictionary<string, int>();
            foreach (var item in watchedItems)
            {
                foreach (var genre in item.Genres)
                {
                    genreCounts.TryGetValue(genre, out var count);
                    genreCounts[genre] = count + 1;
                }
            }

            _output.WriteLine("\n=== USER GENRE PREFERENCES ===");
            foreach (var genre in genreCounts.OrderByDescending(g => g.Value).Take(15))
            {
                var percentage = (double)genre.Value / watchedItems.Count * 100;
                _output.WriteLine($"  {genre.Key}: {genre.Value} ({percentage:F1}%)");
            }

            genreCounts.Should().NotBeEmpty("user should have genre preferences from watch history");
        }

        [SkippableFact]
        public async Task LivePipeline_AnalyzeSpecificRecommendation_ShowsWhyRecommended()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            var library = await FetchLibraryAsync();
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);

            var (watchedItems, userProfile) = await FetchUserWatchHistoryAsync(library, embeddings);

            // Find the anime recommendations
            var devilMayCry = library.FirstOrDefault(i => i.Name.Contains("Devil May Cry"));
            var myHeroAcademia = library.FirstOrDefault(i => i.Name.Contains("My Hero Academia"));

            if (devilMayCry != null)
            {
                _output.WriteLine("\n=== WHY 'Devil May Cry' IS RECOMMENDED ===");
                _output.WriteLine($"Genres: {string.Join(", ", devilMayCry.Genres)}");
                _output.WriteLine($"Actors: {string.Join(", ", devilMayCry.Actors)}");
                _output.WriteLine($"Directors: {string.Join(", ", devilMayCry.Directors)}");
                
                // Find watched items with similar genres
                var similarWatched = watchedItems
                    .Where(w => w.Genres.Intersect(devilMayCry.Genres).Any())
                    .OrderByDescending(w => w.Genres.Intersect(devilMayCry.Genres).Count())
                    .Take(10)
                    .ToList();

                _output.WriteLine("\nYou watched these items with similar genres:");
                foreach (var item in similarWatched)
                {
                    var sharedGenres = item.Genres.Intersect(devilMayCry.Genres).ToList();
                    _output.WriteLine($"  - {item.Name} ({item.ReleaseYear}) - Shared: {string.Join(", ", sharedGenres)}");
                }
            }

            if (myHeroAcademia != null)
            {
                _output.WriteLine("\n=== WHY 'My Hero Academia' IS RECOMMENDED ===");
                _output.WriteLine($"Genres: {string.Join(", ", myHeroAcademia.Genres)}");
                _output.WriteLine($"Actors: {string.Join(", ", myHeroAcademia.Actors)}");
                _output.WriteLine($"Directors: {string.Join(", ", myHeroAcademia.Directors)}");
                
                var similarWatched = watchedItems
                    .Where(w => w.Genres.Intersect(myHeroAcademia.Genres).Any())
                    .OrderByDescending(w => w.Genres.Intersect(myHeroAcademia.Genres).Count())
                    .Take(10)
                    .ToList();

                _output.WriteLine("\nYou watched these items with similar genres:");
                foreach (var item in similarWatched)
                {
                    var sharedGenres = item.Genres.Intersect(myHeroAcademia.Genres).ToList();
                    _output.WriteLine($"  - {item.Name} ({item.ReleaseYear}) - Shared: {string.Join(", ", sharedGenres)}");
                }
            }

            // Show genre distribution in watch history
            var genreCounts = new Dictionary<string, int>();
            foreach (var item in watchedItems)
            {
                foreach (var genre in item.Genres)
                {
                    genreCounts.TryGetValue(genre, out var count);
                    genreCounts[genre] = count + 1;
                }
            }

            _output.WriteLine("\n=== YOUR TOP WATCHED GENRES ===");
            foreach (var genre in genreCounts.OrderByDescending(g => g.Value).Take(15))
            {
                var percentage = (double)genre.Value / watchedItems.Count * 100;
                _output.WriteLine($"  {genre.Key}: {genre.Value} items ({percentage:F1}%)");
            }
        }

        [SkippableFact]
        public async Task LivePipeline_ShowAnimatedWatchHistory_ListsAnimatedContent()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            var library = await FetchLibraryAsync();
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);

            var (watchedItems, _) = await FetchUserWatchHistoryAsync(library, embeddings);

            // Find all watched items with Animation genre
            var animatedWatched = watchedItems
                .Where(i => i.Genres.Contains("Animation"))
                .OrderBy(i => i.Name)
                .ToList();

            _output.WriteLine("\n=== ANIMATED CONTENT IN WATCH HISTORY ===");
            _output.WriteLine($"Total: {animatedWatched.Count} items\n");
            foreach (var item in animatedWatched)
            {
                _output.WriteLine($"  - {item.Name} ({item.ReleaseYear}) [{string.Join(", ", item.Genres)}]");
            }

            // Show all Action & Adventure + Sci-Fi & Fantasy items (anime-like genres)
            var animeGenreItems = watchedItems
                .Where(i => i.Genres.Contains("Action & Adventure") && i.Genres.Contains("Sci-Fi & Fantasy"))
                .Where(i => !i.Genres.Contains("Animation")) // Exclude already listed animation
                .OrderBy(i => i.Name)
                .ToList();

            _output.WriteLine("\n=== ACTION & ADVENTURE + SCI-FI & FANTASY (NON-ANIMATED) ===");
            _output.WriteLine($"Total: {animeGenreItems.Count} items\n");
            foreach (var item in animeGenreItems.Take(20))
            {
                _output.WriteLine($"  - {item.Name} ({item.ReleaseYear}) [{string.Join(", ", item.Genres)}]");
            }

            animatedWatched.Should().NotBeNull();
        }

        [SkippableFact]
        public async Task LivePipeline_SimilarityTest_FindsSimilarMovies()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            var library = await FetchLibraryAsync();
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);

            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);

            // Find a popular movie to use as anchor
            var anchorMovie = library
                .Where(i => i.Type == MediaType.Movie && i.CommunityRating > 7)
                .OrderByDescending(i => i.CommunityRating)
                .FirstOrDefault();

            Skip.If(anchorMovie == null, "No highly-rated movie found");

            _output.WriteLine($"\n=== SIMILAR TO: {anchorMovie!.Name} ({anchorMovie.ReleaseYear}) ===");
            _output.WriteLine($"Genres: {string.Join(", ", anchorMovie.Genres)}");
            _output.WriteLine($"Actors: {string.Join(", ", anchorMovie.Actors)}");
            _output.WriteLine($"Directors: {string.Join(", ", anchorMovie.Directors)}");

            var anchorEmbedding = embeddings[anchorMovie.Id];

            // Find most similar movies
            var similarities = new List<(MediaItemMetadata Item, double Similarity)>();
            foreach (var item in library.Where(i => i.Id != anchorMovie.Id && i.Type == MediaType.Movie))
            {
                var similarity = Utilities.VectorMath.CosineSimilarity(
                    anchorEmbedding.Vector, embeddings[item.Id].Vector);
                similarities.Add((item, similarity));
            }

            var topSimilar = similarities.OrderByDescending(s => s.Similarity).Take(10).ToList();

            _output.WriteLine("\nMost similar movies:");
            foreach (var (item, sim) in topSimilar)
            {
                _output.WriteLine($"  {sim:F4} - {item.Name} ({item.ReleaseYear}) [{string.Join(", ", item.Genres)}]");
            }

            // Validate that similar movies share characteristics
            topSimilar.Should().NotBeEmpty();
            topSimilar.First().Similarity.Should().BeGreaterThan(0.25, "top similar movie should have reasonable similarity");
        }

        [SkippableFact]
        public async Task LivePipeline_Performance_CompletesInReasonableTime()
        {
            Skip.IfNot(_serverAvailable, "Jellyfin server not available");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Fetch library
            var fetchStart = stopwatch.ElapsedMilliseconds;
            var library = await FetchLibraryAsync();
            var fetchTime = stopwatch.ElapsedMilliseconds - fetchStart;

            // Build vocabulary
            var vocabStart = stopwatch.ElapsedMilliseconds;
            var vocabBuilder = new VocabularyBuilder(NullLogger<VocabularyBuilder>.Instance);
            var vocabulary = vocabBuilder.BuildVocabulary(library);
            var vocabTime = stopwatch.ElapsedMilliseconds - vocabStart;

            // Compute embeddings
            var embedStart = stopwatch.ElapsedMilliseconds;
            var embeddingService = new EmbeddingService(NullLogger<EmbeddingService>.Instance);
            var embeddings = embeddingService.ComputeEmbeddings(library, vocabulary);
            var embedTime = stopwatch.ElapsedMilliseconds - embedStart;

            // Fetch watch history
            var watchStart = stopwatch.ElapsedMilliseconds;
            var (watchedItems, userProfile) = await FetchUserWatchHistoryAsync(library, embeddings);
            var watchTime = stopwatch.ElapsedMilliseconds - watchStart;

            // Generate recommendations
            var recStart = stopwatch.ElapsedMilliseconds;
            var movieRecs = GenerateRecommendations(library, embeddings, watchedItems, userProfile, MediaType.Movie, 25);
            var tvRecs = GenerateRecommendations(library, embeddings, watchedItems, userProfile, MediaType.Series, 25);
            var recTime = stopwatch.ElapsedMilliseconds - recStart;

            var totalTime = stopwatch.ElapsedMilliseconds;

            _output.WriteLine("\n=== PERFORMANCE REPORT ===");
            _output.WriteLine($"Library size: {library.Count} items");
            _output.WriteLine($"Watched items: {watchedItems.Count}");
            _output.WriteLine($"Embedding dimensions: {embeddings.Values.First().Dimensions}");
            _output.WriteLine("");
            _output.WriteLine($"Fetch library:     {fetchTime,6} ms");
            _output.WriteLine($"Build vocabulary:  {vocabTime,6} ms");
            _output.WriteLine($"Compute embeddings:{embedTime,6} ms");
            _output.WriteLine($"Fetch watch history:{watchTime,5} ms");
            _output.WriteLine($"Generate recs:     {recTime,6} ms");
            _output.WriteLine($"----------------------------");
            _output.WriteLine($"TOTAL:             {totalTime,6} ms");

            // Performance assertions
            vocabTime.Should().BeLessThan(2000, "vocabulary building should be fast");
            embedTime.Should().BeLessThan(30000, "embedding computation for ~1000 items should be under 30s");
            recTime.Should().BeLessThan(1000, "recommendation generation should be fast");
        }

        #region Helper Methods

        private async Task<List<MediaItemMetadata>> FetchLibraryAsync()
        {
            var library = new List<MediaItemMetadata>();

            // Fetch movies (excluding virtual items)
            var moviesJson = await _httpClient.GetStringAsync(
                "/Items?IncludeItemTypes=Movie&Recursive=true&IsVirtualItem=false&Fields=Genres,People,Tags,ProviderIds,CommunityRating,CriticRating,ProductionYear,Path&Limit=1000");
            var moviesDoc = JsonDocument.Parse(moviesJson);
            foreach (var item in moviesDoc.RootElement.GetProperty("Items").EnumerateArray())
            {
                var metadata = ParseMediaItem(item, MediaType.Movie);
                if (metadata != null)
                {
                    // Extra safety: skip items from virtual-libraries path
                    if (metadata.Path != null && metadata.Path.Contains("virtual-libraries", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    library.Add(metadata);
                }
            }

            // Fetch series (excluding virtual items)
            var seriesJson = await _httpClient.GetStringAsync(
                "/Items?IncludeItemTypes=Series&Recursive=true&IsVirtualItem=false&Fields=Genres,People,Tags,ProviderIds,CommunityRating,CriticRating,ProductionYear,Path&Limit=500");
            var seriesDoc = JsonDocument.Parse(seriesJson);
            foreach (var item in seriesDoc.RootElement.GetProperty("Items").EnumerateArray())
            {
                var metadata = ParseMediaItem(item, MediaType.Series);
                if (metadata != null)
                {
                    // Extra safety: skip items from virtual-libraries path
                    if (metadata.Path != null && metadata.Path.Contains("virtual-libraries", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    library.Add(metadata);
                }
            }

            return library;
        }

        private MediaItemMetadata? ParseMediaItem(JsonElement item, MediaType type)
        {
            if (!item.TryGetProperty("Name", out var nameProp) || nameProp.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var name = nameProp.GetString();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var idStr = item.GetProperty("Id").GetString()!;
            var id = Guid.Parse(idStr);

            var metadata = new MediaItemMetadata(id, name, type);

            // Genres
            if (item.TryGetProperty("Genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            {
                foreach (var genre in genres.EnumerateArray())
                {
                    var genreStr = genre.GetString();
                    if (!string.IsNullOrEmpty(genreStr))
                    {
                        metadata.AddGenre(genreStr);
                    }
                }
            }

            // People (actors and directors)
            if (item.TryGetProperty("People", out var people) && people.ValueKind == JsonValueKind.Array)
            {
                foreach (var person in people.EnumerateArray())
                {
                    var personName = person.GetProperty("Name").GetString();
                    var personType = person.GetProperty("Type").GetString();

                    if (string.IsNullOrEmpty(personName))
                    {
                        continue;
                    }

                    if (personType == "Actor")
                    {
                        metadata.AddActor(personName);
                    }
                    else if (personType == "Director")
                    {
                        metadata.AddDirector(personName);
                    }
                }
            }

            // Tags
            if (item.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    var tagStr = tag.GetString();
                    if (!string.IsNullOrEmpty(tagStr))
                    {
                        metadata.AddTag(tagStr);
                    }
                }
            }

            // Ratings
            if (item.TryGetProperty("CommunityRating", out var communityRating) &&
                communityRating.ValueKind == JsonValueKind.Number)
            {
                metadata.CommunityRating = (float)communityRating.GetDouble();
            }

            if (item.TryGetProperty("CriticRating", out var criticRating) &&
                criticRating.ValueKind == JsonValueKind.Number)
            {
                metadata.CriticRating = (float)criticRating.GetDouble();
            }

            // Year
            if (item.TryGetProperty("ProductionYear", out var year) &&
                year.ValueKind == JsonValueKind.Number)
            {
                metadata.ReleaseYear = year.GetInt32();
            }

            // Provider IDs
            if (item.TryGetProperty("ProviderIds", out var providerIds) &&
                providerIds.ValueKind == JsonValueKind.Object)
            {
                if (providerIds.TryGetProperty("Tmdb", out var tmdb))
                {
                    metadata.TmdbId = tmdb.GetString();
                }

                if (providerIds.TryGetProperty("Tvdb", out var tvdb))
                {
                    metadata.TvdbId = tvdb.GetString();
                }
            }

            // Path
            if (item.TryGetProperty("Path", out var path) && path.ValueKind == JsonValueKind.String)
            {
                metadata.Path = path.GetString();
            }

            return metadata;
        }

        private async Task<(List<MediaItemMetadata> WatchedItems, UserProfile Profile)> FetchUserWatchHistoryAsync(
            List<MediaItemMetadata> library,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings)
        {
            var config = new PluginConfiguration
            {
                FavoriteBoost = 2.0,
                RewatchBoost = 1.5,
                RecencyDecayHalfLifeDays = 365.0
            };

            // Get user ID
            var usersJson = await _httpClient.GetStringAsync("/Users");
            var usersDoc = JsonDocument.Parse(usersJson);
            var userId = usersDoc.RootElement[0].GetProperty("Id").GetString()!;

            // Fetch watched movies and series
            // For movies: Use IsPlayed filter
            // For series: Check if PlayedPercentage > 50% to handle partially watched series
            var watchedJson = await _httpClient.GetStringAsync(
                $"/Users/{userId}/Items?IncludeItemTypes=Movie,Series&Recursive=true&Fields=Genres,UserData&Limit=1000");
            var watchedDoc = JsonDocument.Parse(watchedJson);

            var libraryLookup = library.ToDictionary(i => i.Id);
            var watchedItems = new List<MediaItemMetadata>();
            var weightedVectors = new List<(float[] Vector, float Weight)>();

            foreach (var item in watchedDoc.RootElement.GetProperty("Items").EnumerateArray())
            {
                var idStr = item.GetProperty("Id").GetString()!;
                var id = Guid.Parse(idStr);

                if (!libraryLookup.TryGetValue(id, out var metadata) || !embeddings.TryGetValue(id, out var embedding))
                {
                    continue;
                }

                // Check if item is actually watched
                bool isWatched = false;
                if (item.TryGetProperty("UserData", out var userData))
                {
                    // Primary check: Played flag (respects user marking items as played/unplayed)
                    if (userData.TryGetProperty("Played", out var played))
                    {
                        isWatched = played.GetBoolean();
                    }

                    // Secondary check for series: If Played flag is not set/false, check PlayedPercentage
                    // This handles series where individual episodes were watched but series isn't marked "played"
                    if (!isWatched && userData.TryGetProperty("PlayedPercentage", out var playedPct))
                    {
                        double pct = playedPct.GetDouble();
                        isWatched = pct > WatchedPercentageThreshold;
                    }

                    // NOTE: We do NOT check LastPlayedDate anymore because:
                    // - User may have marked items as "unplayed" which sets Played=false
                    // - But LastPlayedDate persists from the previous watch
                    // - We should respect the user's explicit "unplayed" action
                }

                if (!isWatched)
                {
                    continue; // Skip unwatched items
                }

                watchedItems.Add(metadata);

                // Get user data for weighting
                float weight = 1.0f;
                if (item.TryGetProperty("UserData", out var userDataForWeight))
                {
                    bool isFavorite = userDataForWeight.TryGetProperty("IsFavorite", out var fav) && fav.GetBoolean();
                    int playCount = userDataForWeight.TryGetProperty("PlayCount", out var pc) ? pc.GetInt32() : 1;
                    
                    // Ensure playCount is at least 1 for watched items (Jellyfin sometimes returns 0)
                    if (playCount < 1)
                    {
                        playCount = 1;
                    }

                    DateTime lastPlayed = DateTime.UtcNow;
                    if (userDataForWeight.TryGetProperty("LastPlayedDate", out var lpd))
                    {
                        DateTime.TryParse(lpd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastPlayed);
                    }

                    double daysSince = (DateTime.UtcNow - lastPlayed).TotalDays;
                    weight = (float)Utilities.WeightCalculator.ComputeCombinedWeight(
                        daysSince, config.RecencyDecayHalfLifeDays,
                        isFavorite, (float)config.FavoriteBoost,
                        playCount, (float)config.RewatchBoost);
                }

                weightedVectors.Add((embedding.Vector, weight));
            }

            // Compute taste vector
            float[] tasteVector;
            if (weightedVectors.Any())
            {
                int dim = embeddings.Values.First().Dimensions;
                tasteVector = new float[dim];
                float totalWeight = 0;

                foreach (var (vector, weight) in weightedVectors)
                {
                    for (int i = 0; i < dim; i++)
                    {
                        tasteVector[i] += vector[i] * weight;
                    }

                    totalWeight += weight;
                }

                if (totalWeight > 0)
                {
                    for (int i = 0; i < dim; i++)
                    {
                        tasteVector[i] /= totalWeight;
                    }
                }

                tasteVector = Utilities.VectorMath.Normalize(tasteVector);
            }
            else
            {
                tasteVector = new float[embeddings.Values.First().Dimensions];
            }

            var profile = new UserProfile(Guid.Parse(userId), tasteVector)
            {
                WatchedItemCount = watchedItems.Count
            };

            return (watchedItems, profile);
        }

        private List<ScoredRecommendation> GenerateRecommendations(
            List<MediaItemMetadata> library,
            IReadOnlyDictionary<Guid, ItemEmbedding> embeddings,
            List<MediaItemMetadata> watchedItems,
            UserProfile userProfile,
            MediaType? mediaType,
            int maxResults)
        {
            var watchedIds = watchedItems.Select(w => w.Id).ToHashSet();
            var recommendations = new List<ScoredRecommendation>();

            foreach (var item in library)
            {
                // Filter by media type if specified
                if (mediaType.HasValue && item.Type != mediaType.Value)
                {
                    continue;
                }

                if (watchedIds.Contains(item.Id))
                {
                    continue;
                }

                if (!embeddings.TryGetValue(item.Id, out var embedding))
                {
                    continue;
                }

                // Filter out items with insufficient metadata (no genres AND no actors)
                // These items produce unreliable similarity scores
                if (item.Genres.Count == 0 && item.Actors.Count == 0)
                {
                    continue;
                }

                var similarity = Utilities.VectorMath.CosineSimilarity(userProfile.TasteVector, embedding.Vector);

                // Clamp to valid range
                similarity = Math.Max(0, Math.Min(1, similarity));

                recommendations.Add(new ScoredRecommendation(item.Id, similarity));
            }

            return recommendations.OrderByDescending(r => r.Score).Take(maxResults).ToList();
        }

        #endregion
    }
}
