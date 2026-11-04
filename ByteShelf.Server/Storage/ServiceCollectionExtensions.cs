using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Storage
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStorageServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.AddSingleton(StorageOptions.FromEnvironment());
            services.AddSingleton<ObjectStore>();
            services.AddSingleton<StagingArea>();
            services.AddSingleton<UploadLocks>();
            services.AddSingleton<LinkTokens>();
            services.AddSingleton<DownloadLinks>();
            services.AddSingleton<UploadLinks>();
            services.AddScoped<UploadLinkAccess>();
            services.AddSingleton<MaintenanceGate>();

            services.AddScoped<IShelfRepository, ShelfRepository>();
            services.AddScoped<IDirectoryRepository, DirectoryRepository>();
            services.AddScoped<IFileRepository, FileRepository>();
            services.AddScoped<IUploadRepository, UploadRepository>();
            services.AddScoped<ITrashRepository, TrashRepository>();

            services.AddScoped<ShelfTree>();
            services.AddScoped<ShelfEncryption>();
            services.AddScoped<ObjectCollector>();
            services.AddScoped<IShelfService, ShelfService>();
            services.AddScoped<ITrashService, TrashService>();
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<IDirectoryService, DirectoryService>();
            services.AddScoped<IUploadService, UploadService>();
            services.AddScoped<IStorageMaintenance, StorageMaintenance>();

            services.AddHostedService<StorageMaintenanceWorker>();

            return services;
        }
    }
}
