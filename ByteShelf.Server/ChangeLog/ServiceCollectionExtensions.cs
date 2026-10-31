using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.ChangeLog.Services;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.ChangeLog
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddChangeLogServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.AddScoped<IChangeLogRepository, ChangeLogRepository>();
            services.AddHostedService<ChangeLogCompactionWorker>();

            return services;
        }
    }
}
