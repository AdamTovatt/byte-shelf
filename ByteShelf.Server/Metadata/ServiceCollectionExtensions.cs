using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Metadata
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>What files' contents say about them: the tables, the job that reads them and the signal that wakes it.</summary>
        public static IServiceCollection AddMetadataServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            services.AddReplicationState();

            services.AddSingleton<MetadataReadingSignal>();
            services.AddSingleton<ContentReadRunner>();
            services.TryAddSingleton<IMetadataPlugins, NoMetadataPlugins>();
            services.AddScoped<IObjectMetadataRepository, ObjectMetadataRepository>();
            services.AddScoped<IContentPluginMetadataRepository, ContentPluginMetadataRepository>();
            services.AddScoped<IFilePluginMetadataRepository, FilePluginMetadataRepository>();
            services.AddScoped<MetadataUpkeep>();
            services.AddScoped<ContentReaders>();
            services.AddScoped<MetadataReading>();

            services.AddHostedService<MetadataReadingWorker>();

            return services;
        }
    }
}
