using System.Data.Common;
using ByteShelf.Server.Shared.Root;
using EasyReasy.Database;
using EasyReasy.Database.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, RootFolder root)
        {
            DatabaseTypeHandlers.RegisterAll();

            IDataSourceFactory dataSourceFactory = new SqliteDataSourceFactory();
            DbDataSource dataSource = dataSourceFactory.CreateDataSource(root.ConnectionString);

            services.AddSingleton(root);
            services.AddSingleton(dataSource);
            services.AddScoped<IDbSessionFactory, DbSessionFactory>();

            return services;
        }
    }
}
