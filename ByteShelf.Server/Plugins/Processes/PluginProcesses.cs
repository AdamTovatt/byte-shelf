using System.Text.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using Microsoft.Win32.SafeHandles;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Has plugins do things over contents a plain file holds, each plugin in a child process of its own for each lane
    /// (<see cref="PluginLane"/>, <see cref="PluginWorkers"/>), one thing at a time in each, in turns (<see cref="PluginTurns"/>).
    /// A process does many, until it has had nothing to do for a while; one that was killed or died is replaced.
    /// </summary>
    /// <remarks>
    /// A process that has read contents may keep some of them in its memory for as long as it lives, so contents
    /// that are to leave nothing behind end every process that ever read them (<see cref="StopAsync"/>). What it
    /// was doing over other contents begins anew in another.
    /// </remarks>
    internal sealed class PluginProcesses : IAsyncDisposable
    {
        /// <summary>How long loading a plugin may take, in either lane; a run may take its lane's <see cref="PluginLane.RunLimit"/>.</summary>
        private static readonly TimeSpan LoadLimit = TimeSpan.FromSeconds(60);

        private readonly Lock _lock = new Lock();
        private readonly Dictionary<PluginInLane, PluginTurns> _turns = new Dictionary<PluginInLane, PluginTurns>();
        private readonly PluginClaims _claims = new PluginClaims();
        private readonly PluginWorkers _workers;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly CancellationToken _stopped;

        private readonly ContentsLocks _locks;
        private readonly ObjectStore _objectStore;
        private readonly RootFolder _root;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PluginProcesses> _logger;

        public PluginProcesses(
            ContentsLocks locks,
            ObjectStore objectStore,
            PluginExecutable executable,
            RootFolder root,
            IServiceScopeFactory scopeFactory,
            TimeProvider timeProvider,
            ILogger<PluginProcesses> logger)
        {
            _stopped = _stopping.Token;
            _workers = new PluginWorkers(executable, root, timeProvider, logger);
            _locks = locks;
            _objectStore = objectStore;
            _root = root;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>Takes a turn with the plugin in <paramref name="lane"/> for a run; dispose it once that is done.</summary>
        public PluginTurns.Turn Enter(string pluginId, PluginLane lane, PluginPriority priority)
        {
            lock (_lock)
            {
                PluginInLane key = new PluginInLane(pluginId, lane);
                if (!_turns.TryGetValue(key, out PluginTurns? turns))
                {
                    turns = new PluginTurns(lane);
                    _turns.Add(key, turns);
                }

                return turns.Enter(priority);
            }
        }

        /// <summary>Does <paramref name="run"/> once it is <paramref name="turn"/>'s, in the turn's lane.</summary>
        /// <exception cref="OperationCanceledException">It was stopped, for contents being removed or a server shutting down, which says nothing of the contents.</exception>
        /// <exception cref="PluginUnavailableException">The plugin could not run, whatever the contents.</exception>
        public async Task<T> RunAsync<T>(IPluginRun<T> run, PluginTurns.Turn turn, CancellationToken cancellationToken)
        {
            using CancellationTokenSource stopped = CancellationTokenSource.CreateLinkedTokenSource(_stopped, cancellationToken);
            await turn.Granted.WaitAsync(stopped.Token);

            JsonElement arguments;
            PluginClaims.Claim claim;
            using (await _locks.LockAsync(run.Sha256, stopped.Token))
            {
                IReadOnlyList<string> mediaTypes = await GetMediaTypesAsync(run.Sha256);
                if (mediaTypes.Count == 0 || run.Prepare(mediaTypes) is not { } prepared)
                {
                    return run.Nothing;
                }

                // Known before the lock goes: a removal that takes it next finds the run and ends it.
                arguments = prepared;
                claim = _claims.Add(run.Sha256, stopped);
            }

            try
            {
                return await RunAsync(run, turn.Lane, arguments, claim.Stop.Token);
            }
            finally
            {
                claim.End();
            }
        }

        /// <summary>
        /// Ends everything plugins do with the contents, and every process that ever read them: once this ends nothing
        /// read from them is left in a process or on disk, now or by an earlier run or server. Hold the contents'
        /// lock, so nothing more starts.
        /// </summary>
        /// <exception cref="PluginNotStoppedException">A process is still there, or what it wrote.</exception>
        public async Task StopAsync(string sha256)
        {
            await _claims.StopAsync(sha256);
            await _workers.EndEveryOneGivenAsync(sha256);

            IReadOnlyList<string> live = _workers.LiveFolders;
            foreach (WorkFolder folder in WorkFolder.Find(_root).Where(folder => !live.Contains(folder.Path) && folder.GetContents().Contains(sha256)))
            {
                try
                {
                    folder.Remove();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new PluginNotStoppedException($"What a plugin left in {folder.Path} could not be removed.", exception);
                }
            }
        }

        /// <summary>The contents whose traces plugins' processes may have, in memory or on disk, now or left by an earlier one.</summary>
        public IReadOnlySet<string> GetContentsGiven()
        {
            return WorkFolder.Find(_root).SelectMany(folder => folder.GetContents()).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Ends everything plugins do, and has nothing more be done.</summary>
        public async Task StopAllAsync()
        {
            await _stopping.CancelAsync();
            await _claims.StopAllAsync();
            await _workers.EndAllAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAllAsync();
            _stopping.Dispose();
        }

        private async Task<IReadOnlyList<string>> GetMediaTypesAsync(string sha256)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPlainContentsRepository>().GetMediaTypesAsync(sha256);
        }

        private async Task<T> RunAsync<T>(IPluginRun<T> run, PluginLane lane, JsonElement arguments, CancellationToken cancellationToken)
        {
            SafeFileHandle contents;
            try
            {
                contents = File.OpenHandle(_objectStore.GetPath(run.Sha256), options: FileOptions.Asynchronous);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("The plugin {Plugin} was not given the contents {Sha256}, which could not be opened ({Exception}).", run.Plugin.Id, run.Sha256, exception.GetType().FullName);
                return run.Nothing;
            }

            using (contents)
            {
                RunRequest request = new RunRequest(ProcessWork.NameOf(run.Work), arguments, RandomAccess.GetLength(contents));
                while (true)
                {
                    (PluginWorker worker, PluginWorker.Lease lease, bool isNew) = await _workers.GetAsync(run.Plugin, lane, run.Sha256, LoadLimit, cancellationToken);
                    bool goesOn = false;
                    try
                    {
                        using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(lease.Stop, cancellationToken);
                        (PluginRunResult? result, bool processGoesOn) = await lease.Process.RunAsync(request, contents, lane.RunLimit, either.Token);
                        if (lease.Stop.IsCancellationRequested)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            continue;
                        }

                        if (result == null)
                        {
                            if (isNew)
                            {
                                throw new PluginUnavailableException("its process ended before it began");
                            }

                            // It died as it waited for something to do.
                            continue;
                        }

                        Log(run.Plugin, run.Sha256, result);
                        goesOn = processGoesOn;
                        return await run.TakeAsync(result, worker.Folder, cancellationToken);
                    }
                    catch (OperationCanceledException) when (lease.Stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Ended for other contents it had read: whatever came of it is done anew.
                    }
                    finally
                    {
                        await _workers.ReturnAsync(worker, lease, goesOn);
                    }
                }
            }
        }

        private void Log(PluginTarget plugin, string sha256, PluginRunResult result)
        {
            switch (result)
            {
                case PluginRunResult.Failed failed:
                    _logger.LogInformation("The plugin {Plugin} could not do anything with the contents {Sha256}: {Reason}.", plugin.Id, sha256, failed.Reason);
                    break;
                case PluginRunResult.ContentsNotRead:
                    _logger.LogWarning("The plugin {Plugin} was not given all of the contents {Sha256}, which could not be read.", plugin.Id, sha256);
                    break;
            }
        }
    }
}
