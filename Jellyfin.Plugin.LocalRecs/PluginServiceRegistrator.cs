using System;
using System.IO;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.LocalRecs.ScheduledTasks;
using Jellyfin.Plugin.LocalRecs.Services;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LocalRecs
{
    /// <summary>
    /// Register services for dependency injection.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Phase 3: Embedding Layer Services
            serviceCollection.AddSingleton<LibraryAnalysisService>();
            serviceCollection.AddSingleton<VocabularyBuilder>();
            serviceCollection.AddSingleton<EmbeddingService>();

            // Phase 4: User Profile Service
            serviceCollection.AddSingleton<UserProfileService>();

            // Phase 5: Recommendation Engine
            serviceCollection.AddSingleton<RecommendationEngine>();

            // Phase 6: Recommendation Refresh Service
            serviceCollection.AddSingleton<RecommendationRefreshService>();

            // NFO Writer for metadata files
            serviceCollection.AddSingleton<NfoWriter>();

            // Phase 7: Virtual Library Services
            // Use lazy initialization to ensure Plugin.Instance is available
            serviceCollection.AddSingleton(sp =>
            {
                var virtualLibraryBasePath = GetVirtualLibraryBasePath(sp);
                return new VirtualLibraryManager(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VirtualLibraryManager>>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>(),
                    sp.GetRequiredService<NfoWriter>(),
                    virtualLibraryBasePath);
            });

            serviceCollection.AddSingleton(sp =>
            {
                var virtualLibraryBasePath = GetVirtualLibraryBasePath(sp);
                var service = new PlayStatusSyncService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PlayStatusSyncService>>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.IUserDataManager>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.IUserManager>(),
                    virtualLibraryBasePath);

                // Service will be initialized when VirtualLibraryInitializer resolves it
                service.Initialize();

                return service;
            });

            serviceCollection.AddHostedService(sp =>
            {
                var virtualLibraryBasePath = GetVirtualLibraryBasePath(sp);
                return new VirtualLibraryInitializer(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VirtualLibraryInitializer>>(),
                    sp.GetRequiredService<MediaBrowser.Controller.Library.IUserManager>(),
                    virtualLibraryBasePath,
                    sp.GetRequiredService<VirtualLibraryManager>(),
                    sp.GetRequiredService<PlayStatusSyncService>());
            });

            // User Lifecycle Event Handlers
            serviceCollection.AddScoped<IEventConsumer<UserCreatedEventArgs>>(sp =>
                new UserCreatedEventHandler(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserCreatedEventHandler>>(),
                    sp.GetRequiredService<VirtualLibraryManager>()));

            serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>>(sp =>
                new UserDeletedEventHandler(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserDeletedEventHandler>>(),
                    sp.GetRequiredService<VirtualLibraryManager>()));

            // Phase 8: Scheduled Tasks
            serviceCollection.AddTransient<IScheduledTask, RecommendationRefreshTask>();
            serviceCollection.AddTransient<IScheduledTask, BenchmarkTask>();
        }

        private static string GetVirtualLibraryBasePath(IServiceProvider serviceProvider)
        {
            // Get plugin data directory and create virtual library subdirectory
            // Use IApplicationPaths to deterministically compute the path
            // This is consistent with how BasePlugin.DataFolderPath works internally
            var appPaths = serviceProvider.GetRequiredService<IApplicationPaths>();
            var pluginDataPath = Path.Combine(appPaths.PluginsPath, "LocalRecs");
            var virtualLibraryBasePath = Path.Combine(pluginDataPath, "virtual-libraries");
            return virtualLibraryBasePath;
        }
    }
}
