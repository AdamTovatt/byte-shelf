using ByteShelf.Server.Jobs.Repositories;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Jobs
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddJobServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.AddScoped<IJobRunRepository, JobRunRepository>();
            services.AddScoped<IJobRunRecorder, JobRunRecorder>();

            // The sweeper must be registered before any hosted service that records runs, so
            // orphans from the previous process are closed before new rows appear.
            services.AddHostedService<JobRunStartupSweeper>();

            return services;
        }
    }
}
