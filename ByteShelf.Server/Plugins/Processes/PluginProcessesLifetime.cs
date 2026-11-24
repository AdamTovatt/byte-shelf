using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Removes what plugins' processes were working on when the server last ended without stopping, before any starts;
    /// and ends them as soon as the server begins to stop, so that nothing the system does to its processes then
    /// is taken for the contents' doing.
    /// </summary>
    internal sealed class PluginProcessesLifetime : IHostedLifecycleService
    {
        private const int RemoveAttempts = 3;
        private static readonly TimeSpan RemoveInterval = TimeSpan.FromMilliseconds(100);

        private readonly RootFolder _root;
        private readonly PluginProcesses _processes;
        private readonly ILogger<PluginProcessesLifetime> _logger;

        public PluginProcessesLifetime(RootFolder root, PluginProcesses processes, ILogger<PluginProcessesLifetime> logger)
        {
            _root = root;
            _processes = processes;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (WorkFolder folder in WorkFolder.Find(_root))
            {
                await RemoveAsync(folder, cancellationToken);
            }
        }

        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return _processes.StopAllAsync();
        }

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StartedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task RemoveAsync(WorkFolder folder, CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    folder.Remove();
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (attempt == RemoveAttempts)
                    {
                        _logger.LogError(exception, "Could not remove {Folder}, which holds what was read from contents; remove it.", folder.Path);
                        return;
                    }
                }

                await Task.Delay(RemoveInterval, cancellationToken);
            }
        }
    }
}
