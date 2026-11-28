using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Storage.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Derived
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>What plugins make from files' contents: making it, keeping it, serving it and removing it.</summary>
        public static IServiceCollection AddDerivedServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.TryAddSingleton<IDerivedFilePlugins, NoDerivedFilePlugins>();
            services.AddSingleton<DerivedMakers>();
            services.AddScoped<IDerivedSizeRepository, DerivedSizeRepository>();
            services.AddSingleton<DerivedStore>();
            services.AddSingleton<ProcessWork, MakeWork>();
            services.AddScoped<DerivedRoom>();
            services.AddScoped<DerivedFiles>();
            services.AddSingleton<MakerRuns>();
            services.AddSingleton<DerivedMaking>();
            services.AddSingleton<IDerivedListing, DerivedListing>();
            services.AddSingleton<DerivedChanges>();
            services.AddSingleton<StoredContents>();
            services.AddSingleton<IStoredContentsListener>(provider => provider.GetRequiredService<StoredContents>());
            services.AddScoped<IContentsTraces, ContentsTraces>();
            services.AddScoped<IDerivedFileService, DerivedFileService>();
            services.AddScoped<DerivedBackground>();
            services.AddScoped<DerivedMaintenance>();

            services.AddHostedService<StoredContentsWorker>();
            foreach (MakingGroup group in MakingGroup.InBackground)
            {
                services.AddSingleton<IHostedService>(provider => ActivatorUtilities.CreateInstance<DerivedMakingWorker>(provider, group));
            }

            services.AddHostedService<DerivedMaintenanceWorker>();

            return services;
        }
    }
}
