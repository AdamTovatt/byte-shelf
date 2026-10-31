using System.Globalization;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Storage
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStorageServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.AddSingleton(CreateOptions());
            services.AddSingleton<ObjectStore>();
            services.AddSingleton<StagingArea>();
            services.AddSingleton<UploadLocks>();
            services.AddSingleton<DownloadLinks>();

            services.AddScoped<IShelfRepository, ShelfRepository>();
            services.AddScoped<IDirectoryRepository, DirectoryRepository>();
            services.AddScoped<IFileRepository, FileRepository>();
            services.AddScoped<IUploadRepository, UploadRepository>();
            services.AddScoped<ITrashRepository, TrashRepository>();

            services.AddScoped<ShelfTree>();
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

        private static StorageOptions CreateOptions()
        {
            string? restoreWindowDays = EnvironmentVariables.RestoreWindowDays.GetValueOrDefault();
            if (string.IsNullOrWhiteSpace(restoreWindowDays))
            {
                return new StorageOptions();
            }

            if (!int.TryParse(restoreWindowDays, NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days < 1)
            {
                throw new InvalidOperationException($"BYTESHELF_RESTORE_WINDOW_DAYS must be a whole number of days, at least 1; it is '{restoreWindowDays}'.");
            }

            return new StorageOptions { RestoreWindow = TimeSpan.FromDays(days) };
        }
    }
}
