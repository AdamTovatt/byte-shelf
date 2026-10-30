using ByteShelf.Server.Version.Services;

namespace ByteShelf.Server.Version
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVersionServices(this IServiceCollection services)
        {
            services.AddSingleton<IVersionService, VersionService>();
            return services;
        }
    }
}
