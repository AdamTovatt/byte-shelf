using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// The plugins' processes: one for each plugin in each lane at most, used by one run at a time, kept until it has had
    /// nothing to do for a while, and known until it has ended and its folder is gone.
    /// </summary>
    internal sealed class PluginWorkers
    {
        /// <summary>How long a process is kept that has nothing to do.</summary>
        private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(60);

        private readonly Lock _lock = new Lock();
        private readonly Dictionary<PluginInLane, PluginWorker> _live = new Dictionary<PluginInLane, PluginWorker>();
        private readonly List<PluginWorker> _ending = new List<PluginWorker>();
        private readonly PluginExecutable _executable;
        private readonly RootFolder _root;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;

        public PluginWorkers(PluginExecutable executable, RootFolder root, TimeProvider timeProvider, ILogger logger)
        {
            _executable = executable;
            _root = root;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>The folders of the processes that may still be used.</summary>
        public IReadOnlyList<string> LiveFolders
        {
            get
            {
                lock (_lock)
                {
                    return _live.Values.Select(worker => worker.Folder.Path).ToList();
                }
            }
        }

        /// <summary>
        /// The plugin's process in <paramref name="lane"/>, to give the contents <paramref name="sha256"/>; a new one, loaded,
        /// when it has none it can go on with. Give the lease back once it is done with (<see cref="ReturnAsync"/>).
        /// </summary>
        /// <exception cref="PluginUnavailableException">No process could be had.</exception>
        public async Task<(PluginWorker Worker, PluginWorker.Lease Lease, bool IsNew)> GetAsync(PluginTarget plugin, PluginLane lane, string sha256, TimeSpan loadLimit, CancellationToken cancellationToken)
        {
            PluginWorker? previous;
            lock (_lock)
            {
                previous = _live.GetValueOrDefault(new PluginInLane(plugin.Id, lane));
            }

            if (previous != null)
            {
                if (previous.Plugin == plugin && await UseAsync(previous, sha256) is { } used)
                {
                    return (previous, used, false);
                }

                await TryEndAsync(previous);
            }

            cancellationToken.ThrowIfCancellationRequested();
            PluginWorker started = Start(plugin, lane);
            PluginWorker.Lease lease = await UseAsync(started, sha256) ?? throw new PluginUnavailableException("its working folder was removed");
            try
            {
                await lease.Process.LoadedAsync(loadLimit, cancellationToken);
                return (started, lease, true);
            }
            catch
            {
                await ReturnAsync(started, lease, goesOn: false);
                throw;
            }
        }

        /// <summary>
        /// Takes back a process that has nothing to do now, its folder emptied, to be ended once it has had nothing to do
        /// for a while; one that does not go on, or whose folder cannot be emptied, is ended now.
        /// </summary>
        public async Task ReturnAsync(PluginWorker worker, PluginWorker.Lease lease, bool goesOn)
        {
            if (goesOn)
            {
                try
                {
                    worker.Folder.Empty();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "What a plugin left in {Folder} could not be removed, so its process is ended.", worker.Folder.Path);
                    goesOn = false;
                }
            }

            lease.Dispose();
            if (goesOn)
            {
                worker.Rest(_timeProvider, IdleLimit, () => _ = EndIdleAsync(worker));
            }
            else
            {
                await TryEndAsync(worker);
            }
        }

        /// <summary>Ends every process that was given the contents.</summary>
        /// <exception cref="PluginNotStoppedException">A process is still there, or its folder is.</exception>
        public async Task EndEveryOneGivenAsync(string sha256)
        {
            List<PluginWorker> workers;
            lock (_lock)
            {
                workers = _live.Values.Concat(_ending).Where(worker => worker.HasRead(sha256)).ToList();
            }

            foreach (PluginWorker worker in workers)
            {
                await EndAsync(worker);
            }
        }

        /// <summary>Ends every process, logging one that will not end.</summary>
        public async Task EndAllAsync()
        {
            List<PluginWorker> workers;
            lock (_lock)
            {
                workers = _live.Values.ToList();
            }

            foreach (PluginWorker worker in workers)
            {
                await TryEndAsync(worker);
            }
        }

        private PluginWorker Start(PluginTarget plugin, PluginLane lane)
        {
            WorkFolder folder;
            try
            {
                folder = WorkFolder.Create(_root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new PluginUnavailableException("its working folder could not be made", exception);
            }

            PluginWorker started;
            try
            {
                started = new PluginWorker(plugin, lane, PluginProcess.Start(_executable, plugin, lane, folder, _timeProvider));
            }
            catch
            {
                folder.Remove();
                throw;
            }

            lock (_lock)
            {
                _live[started.Key] = started;
            }

            return started;
        }

        /// <summary>Uses the process for the contents; null when it can be used no more. One they could not be noted of is ended.</summary>
        private async Task<PluginWorker.Lease?> UseAsync(PluginWorker worker, string sha256)
        {
            try
            {
                return worker.TryUse(sha256);
            }
            catch (PluginUnavailableException)
            {
                await TryEndAsync(worker);
                throw;
            }
        }

        /// <summary>Ends a process that had nothing to do for a while, from its timer: so nothing it throws goes unseen.</summary>
        private async Task EndIdleAsync(PluginWorker worker)
        {
            try
            {
                await TryEndAsync(worker);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The idle process of the plugin {Plugin} could not be ended.", worker.Plugin.Id);
            }
        }

        private async Task TryEndAsync(PluginWorker worker)
        {
            try
            {
                await EndAsync(worker);
            }
            catch (PluginNotStoppedException exception)
            {
                _logger.LogError(exception, "What the plugin {Plugin} read from contents may be left, in a process or in {Folder}; end or remove it.", worker.Plugin.Id, worker.Folder.Path);
            }
        }

        /// <summary>Ends a process and removes its folder. It is known until both are done.</summary>
        /// <exception cref="PluginNotStoppedException">It is still there, or its folder is.</exception>
        private async Task EndAsync(PluginWorker worker)
        {
            lock (_lock)
            {
                if (_live.GetValueOrDefault(worker.Key) == worker)
                {
                    _live.Remove(worker.Key);
                }

                if (!_ending.Contains(worker))
                {
                    _ending.Add(worker);
                }
            }

            await worker.EndAsync();
            lock (_lock)
            {
                _ending.Remove(worker);
            }
        }
    }
}
