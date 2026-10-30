using ByteShelf.Server.Invalidation.Services;

namespace ByteShelf.Server.Invalidation
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInvalidationServices(this IServiceCollection services)
        {
            services.AddSingleton<IInvalidationBroadcaster, InMemoryInvalidationBroadcaster>();
            return services;
        }
    }
}
