using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Plugins.Processes
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>The child processes plugins do things over contents in, and what tells admins of a part that cannot run in one.</summary>
        public static IServiceCollection AddPluginProcesses(this IServiceCollection services)
        {
            services.TryAddTimeProvider();
            services.AddSingleton<PluginProblems>();
            services.AddSingleton(_ => PluginExecutable.OfThisServer());
            services.AddSingleton<PluginProcesses>();

            // Before whatever has them do something, so nothing does when it empties the folder that is done in.
            services.AddHostedService<PluginProcessesLifetime>();
            return services;
        }
    }
}
