using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Metadata
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>What files' contents say about them: the table, the job that reads them and the signal that wakes it.</summary>
        public static IServiceCollection AddMetadataServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            services.AddReplicationState();

            services.AddSingleton<MetadataReadingSignal>();
            services.AddScoped<IObjectMetadataRepository, ObjectMetadataRepository>();
            services.AddScoped<MetadataReading>();

            services.AddHostedService<MetadataReadingWorker>();

            return services;
        }
    }
}
