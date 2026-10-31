using System.Data.Common;
using ByteShelf.Server.Shared.Root;
using EasyReasy.Database;

namespace ByteShelf.Server.Shared.Database
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, RootFolder root)
        {
            DbDataSource dataSource = DataSources.Create(root.ConnectionString);

            services.AddSingleton(root);
            services.AddSingleton(dataSource);
            services.AddScoped<IDbSessionFactory, DbSessionFactory>();

            return services;
        }
    }
}
