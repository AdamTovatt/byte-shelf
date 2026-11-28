using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Jobs.Services;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Makes what is missing of one group made in the background (<see cref="MakingGroup.InBackground"/>) from files
    /// already stored, at startup (a plugin may be new, or another version) and hourly, which picks up an emptied folder
    /// and anything a failed pass left. Each plugin goes at its own pace, and each group has a job of its own, so a pass
    /// of one that takes days holds up no other's.
    /// </summary>
    public class DerivedMakingWorker : PeriodicJobWorker<DerivedMakingSummary>
    {
        private readonly MakingGroup _group;
        private readonly string _job;
        private readonly IDerivedFilePlugins _plugins;

        /// <exception cref="ArgumentException">The group is not made in the background.</exception>
        public DerivedMakingWorker(MakingGroup group, IServiceScopeFactory scopeFactory, IDerivedFilePlugins plugins, TimeProvider timeProvider, ILogger<DerivedMakingWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _group = group;
            _job = group.Start.Job ?? throw new ArgumentException($"The group {group} is not made in the background.", nameof(group));
            _plugins = plugins;
        }

        /// <summary>The group it makes.</summary>
        internal MakingGroup Group => _group;

        protected override string JobName => _job;

        protected override TimeSpan Interval => TimeSpan.FromHours(1);

        protected override async Task<DerivedMakingSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            int[] made = await Task.WhenAll(_plugins.All.Where(plugin => plugin.MakesAny(_group)).Select(plugin => RunPluginAsync(services, plugin, cancellationToken)));
            return new DerivedMakingSummary(made.Sum());
        }

        private async Task<int> RunPluginAsync(IServiceProvider services, DerivedFilePlugin plugin, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeBacklogAsync(plugin, _group, cancellationToken);
        }
    }
}
