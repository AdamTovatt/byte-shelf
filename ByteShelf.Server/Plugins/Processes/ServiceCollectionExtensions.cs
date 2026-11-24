using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Plugins.Processes
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>The child processes plugins do things over contents in.</summary>
        public static IServiceCollection AddPluginProcesses(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            services.AddSingleton<ContentsLocks>();
            services.AddScoped<ContentsTransactions>();
            services.AddSingleton(_ => PluginExecutable.OfThisServer());
            services.AddSingleton<PluginProcesses>();

            // Before whatever has them do something, so nothing does when it empties the folder that is done in.
            services.AddHostedService<PluginProcessesLifetime>();
            return services;
        }
    }
}
