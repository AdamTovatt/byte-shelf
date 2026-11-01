using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Jobs
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>What every <see cref="Services.PeriodicJobWorker{TSummary}"/> needs: the clock.</summary>
        public static IServiceCollection AddJobServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            return services;
        }
    }
}
