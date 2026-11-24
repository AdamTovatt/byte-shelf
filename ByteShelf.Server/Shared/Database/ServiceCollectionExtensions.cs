using System.Data.Common;
using ByteShelf.Server.Shared.Root;
using EasyReasy.Database;

namespace ByteShelf.Server.Shared.Database
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, RootFolder root)
        {
            services.AddSingleton(root);
            services.AddSingleton<DbDataSource>(root.Database);
            services.AddScoped<IDbSessionFactory, DbSessionFactory>();

            return services;
        }
    }
}
