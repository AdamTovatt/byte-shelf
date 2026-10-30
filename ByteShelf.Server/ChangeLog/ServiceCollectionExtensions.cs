using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.ChangeLog
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddChangeLogServices(this IServiceCollection services)
        {
            services.TryAddTimeProvider();

            services.AddScoped<IChangeLogRepository, ChangeLogRepository>();

            return services;
        }
    }
}
