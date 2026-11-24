using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Removes what makers were working on when the server last ended without stopping, before anything is made;
    /// and ends what is being made as soon as the server begins to stop, so that nothing the system does to its
    /// processes then is taken for the contents' doing.
    /// </summary>
    internal sealed class MakingLifetime : IHostedLifecycleService
    {
        private const int RemoveAttempts = 3;
        private static readonly TimeSpan RemoveInterval = TimeSpan.FromMilliseconds(100);

        private readonly RootFolder _root;
        private readonly DerivedMaking _making;
        private readonly ILogger<MakingLifetime> _logger;

        public MakingLifetime(RootFolder root, DerivedMaking making, ILogger<MakingLifetime> logger)
        {
            _root = root;
            _making = making;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (MakingFolder folder in MakingFolder.Find(_root))
            {
                await RemoveAsync(folder, cancellationToken);
            }
        }

        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return _making.StopAllAsync();
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

        private async Task RemoveAsync(MakingFolder folder, CancellationToken cancellationToken)
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
