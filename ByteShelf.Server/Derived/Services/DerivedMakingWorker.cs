using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Makes what is missing from files already stored, at startup (a plugin may be new, or another version) and
    /// hourly, which picks up an emptied folder and anything a failed pass left. Each plugin goes at its own
    /// pace.
    /// </summary>
    public class DerivedMakingWorker : PeriodicJobWorker<DerivedMakingSummary>
    {
        private readonly IDerivedFilePlugins _plugins;

        public DerivedMakingWorker(IServiceScopeFactory scopeFactory, IDerivedFilePlugins plugins, TimeProvider timeProvider, ILogger<DerivedMakingWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _plugins = plugins;
        }

        protected override string JobName => JobNames.DerivedMaking;

        protected override TimeSpan Interval => TimeSpan.FromHours(1);

        protected override async Task<DerivedMakingSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            int[] made = await Task.WhenAll(_plugins.All.Select(plugin => RunPluginAsync(services, plugin, cancellationToken)));
            return new DerivedMakingSummary(made.Sum());
        }

        private static async Task<int> RunPluginAsync(IServiceProvider services, DerivedFilePlugin plugin, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeBacklogAsync(plugin, cancellationToken);
        }
    }
}
