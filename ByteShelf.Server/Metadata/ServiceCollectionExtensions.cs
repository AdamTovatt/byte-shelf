using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Shared;
using ByteShelfCommon;

namespace ByteShelf.Server.Metadata
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// What files' contents say about them: the tables, the job that reads them and the signal that wakes
        /// it. Which plugins there are is registered apart, by <see cref="AddInstalledPlugins"/> with
        /// <see cref="IPluginReaders"/>, or <see cref="AddPluginsNotKnown"/>.
        /// </summary>
        public static IServiceCollection AddMetadataServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            services.AddReplicationState();

            services.AddSingleton<MetadataReadingSignal>();
            services.AddSingleton<ContentReadRunner>();
            services.AddSingleton<ReaderProblems>();
            services.AddScoped<IObjectMetadataRepository, ObjectMetadataRepository>();
            services.AddScoped<IContentPluginMetadataRepository, ContentPluginMetadataRepository>();
            services.AddScoped<IFilePluginMetadataRepository, FilePluginMetadataRepository>();
            services.AddScoped<IPluginFieldValueRepository, PluginFieldValueRepository>();
            services.AddScoped<PluginFieldIndex>();
            services.AddScoped<MetadataUpkeep>();
            services.AddScoped<FileMetadataService>();
            services.AddScoped<ContentReaders>();
            services.AddScoped<MetadataReading>();

            services.AddHostedService<PluginFieldIndexStartup>();
            services.AddHostedService<MetadataReadingWorker>();

            return services;
        }

        /// <summary>Says the plugins are <paramref name="installed"/>.</summary>
        public static IServiceCollection AddInstalledPlugins(this IServiceCollection services, InstalledPlugins installed)
        {
            services.AddSingleton(installed);
            services.AddSingleton(KnownPlugins.Of(installed));
            return services;
        }

        /// <summary>
        /// Says it is not known which plugins there are, as in a command. Plugins' documents are removed there,
        /// not written, and no files are listed; what needs to know the plugins, as searching files, showing
        /// their metadata and reading their contents do, cannot be made.
        /// </summary>
        public static IServiceCollection AddPluginsNotKnown(this IServiceCollection services)
        {
            services.AddSingleton(KnownPlugins.NotKnown);
            return services;
        }
    }
}
