using ByteShelf.Server.Replication;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Storage
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStorageServices(this IServiceCollection services, StorageOptions options)
        {
            services.TryAddTimeProvider();
            services.AddReplicationState();

            services.AddSingleton(options);
            services.AddSingleton<ObjectStore>();
            services.AddSingleton<StagingArea>();
            services.AddSingleton<UploadLocks>();
            services.AddSingleton<LinkTokens>();
            services.AddScoped<DownloadLinks>();
            services.AddScoped<UploadLinks>();
            services.AddScoped<LinkAccess>();
            services.AddScoped<ILinkService, LinkService>();
            services.AddSingleton<MaintenanceGate>();

            services.AddScoped<IShelfRepository, ShelfRepository>();
            services.AddScoped<IDirectoryRepository, DirectoryRepository>();
            services.AddScoped<IFileRepository, FileRepository>();
            services.AddScoped<IPlainContentsRepository, PlainContentsRepository>();
            services.AddScoped<IUploadRepository, UploadRepository>();
            services.AddScoped<ITrashRepository, TrashRepository>();
            services.AddScoped<ILinkRepository, LinkRepository>();

            services.AddScoped<ShelfTree>();
            services.AddScoped<ShelfEncryption>();
            services.AddScoped<ObjectCollector>();
            services.AddScoped<IShelfService, ShelfService>();
            services.AddScoped<ITrashService, TrashService>();
            services.AddScoped<ListedFiles>();
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<IDirectoryService, DirectoryService>();
            services.AddScoped<EntryBatches>();
            services.AddScoped<IUploadService, UploadService>();
            services.AddScoped<IStorageMaintenance, StorageMaintenance>();

            services.AddHostedService<StorageMaintenanceWorker>();

            return services;
        }
    }
}
