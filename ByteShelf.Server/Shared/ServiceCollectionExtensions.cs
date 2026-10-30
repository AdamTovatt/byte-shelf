using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Shared
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="TimeProvider.System"/> unless a <see cref="TimeProvider"/> is registered
        /// already, so every domain can ask for the clock and a test can register a fake one first.
        /// </summary>
        public static IServiceCollection TryAddTimeProvider(this IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            return services;
        }
    }
}
