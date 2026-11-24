using System.Text.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using Microsoft.Win32.SafeHandles;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Has plugins do things over contents a plain file holds, each plugin in a child process of its own, one thing
    /// at a time. A process does many, until it has had nothing to do for a while; one that was killed or died is replaced.
    /// </summary>
    /// <remarks>
    /// A process that has read contents may keep some of them in its memory for as long as it lives, so contents
    /// that are to leave nothing behind end every process that ever read them (<see cref="StopAsync"/>). What it
    /// was doing over other contents begins anew in another.
    /// </remarks>
    internal sealed class PluginProcesses : IAsyncDisposable
    {
        /// <summary>How long a run may take, and loading a plugin before it.</summary>
        private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);

        /// <summary>How long a process is kept that has nothing to do.</summary>
        private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(60);

        private readonly Lock _lock = new Lock();
        private readonly Dictionary<string, PluginTurns> _turns = new Dictionary<string, PluginTurns>(StringComparer.Ordinal);
        private readonly Dictionary<string, PluginWorker> _live = new Dictionary<string, PluginWorker>(StringComparer.Ordinal);
        private readonly List<PluginWorker> _ending = new List<PluginWorker>();
        private readonly HashSet<Claim> _claims = new HashSet<Claim>();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly CancellationToken _stopped;

        private readonly ContentsLocks _locks;
        private readonly ObjectStore _objectStore;
        private readonly PluginExecutable _executable;
        private readonly RootFolder _root;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeProvider _timeProvider;
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
            _locks = locks;
            _objectStore = objectStore;
            _executable = executable;
            _root = root;
            _scopeFactory = scopeFactory;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>Takes a turn with the plugin for a run; dispose it once that is done.</summary>
        public PluginTurns.Turn Enter(string pluginId, PluginPriority priority)
        {
            lock (_lock)
            {
                if (!_turns.TryGetValue(pluginId, out PluginTurns? turns))
                {
                    turns = new PluginTurns();
                    _turns.Add(pluginId, turns);
                }

                return turns.Enter(priority);
            }
        }

        /// <summary>Does <paramref name="run"/> once it is <paramref name="turn"/>'s.</summary>
        /// <exception cref="OperationCanceledException">It was stopped, for contents being removed or a server shutting down, which says nothing of the contents.</exception>
        /// <exception cref="PluginUnavailableException">The plugin could not run, whatever the contents.</exception>
        public async Task<T> RunAsync<T>(IPluginRun<T> run, PluginTurns.Turn turn, CancellationToken cancellationToken)
        {
            using CancellationTokenSource stopped = CancellationTokenSource.CreateLinkedTokenSource(_stopped, cancellationToken);
            await turn.Granted.WaitAsync(stopped.Token);

            JsonElement arguments;
            Claim claim;
            using (await _locks.LockAsync(run.Sha256, stopped.Token))
            {
                IReadOnlyList<string> mediaTypes = await GetMediaTypesAsync(run.Sha256);
                if (mediaTypes.Count == 0 || run.Prepare(mediaTypes) is not { } prepared)
                {
                    return run.Nothing;
                }

                // Known before the lock goes: a removal that takes it next finds the run and ends it.
                arguments = prepared;
                claim = AddClaim(run.Sha256, stopped);
            }

            try
            {
                return await RunAsync(run, arguments, claim.Stop.Token);
            }
            finally
            {
                lock (_lock)
                {
                    _claims.Remove(claim);
                }

                claim.Ended.SetResult();
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
            List<Claim> claims;
            lock (_lock)
            {
                claims = _claims.Where(claim => claim.Sha256 == sha256).ToList();
            }

            await StopAsync(claims);

            List<PluginWorker> workers;
            lock (_lock)
            {
                workers = _live.Values.Concat(_ending).Where(worker => worker.HasRead(sha256)).ToList();
            }

            foreach (PluginWorker worker in workers)
            {
                await EndAsync(worker);
            }

            List<string> live;
            lock (_lock)
            {
                live = _live.Values.Select(worker => worker.Folder.Path).ToList();
            }

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

            List<Claim> claims;
            lock (_lock)
            {
                claims = _claims.ToList();
            }

            await StopAsync(claims);

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

        private static async Task StopAsync(IReadOnlyList<Claim> claims)
        {
            foreach (Claim claim in claims)
            {
                try
                {
                    await claim.Stop.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // It ended just now.
                }
            }

            foreach (Claim claim in claims)
            {
                await claim.Ended.Task;
            }
        }

        private Claim AddClaim(string sha256, CancellationTokenSource stop)
        {
            lock (_lock)
            {
                stop.Token.ThrowIfCancellationRequested();
                Claim claim = new Claim(sha256, stop);
                _claims.Add(claim);
                return claim;
            }
        }

        private async Task<T> RunAsync<T>(IPluginRun<T> run, JsonElement arguments, CancellationToken cancellationToken)
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
                RunRequest request = new RunRequest(run.Work, arguments, RandomAccess.GetLength(contents));
                while (true)
                {
                    (PluginWorker worker, PluginWorker.Lease lease, bool isNew) = await GetAsync(run.Plugin, run.Sha256, cancellationToken);
                    bool goesOn = false;
                    try
                    {
                        using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(lease.Stop, cancellationToken);
                        (PluginRunResult? result, bool processGoesOn) = await lease.Process.RunAsync(request, contents, RunLimit, either.Token);
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
                        await ReturnAsync(worker, lease, goesOn);
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

        /// <summary>The plugin's process, to give the contents <paramref name="sha256"/>; a new one when it has none it can go on with.</summary>
        private async Task<(PluginWorker Worker, PluginWorker.Lease Lease, bool IsNew)> GetAsync(PluginTarget plugin, string sha256, CancellationToken cancellationToken)
        {
            PluginWorker? previous;
            lock (_lock)
            {
                previous = _live.GetValueOrDefault(plugin.Id);
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
            PluginWorker started = Start(plugin);
            PluginWorker.Lease lease = await UseAsync(started, sha256) ?? throw new PluginUnavailableException("its working folder was removed");
            try
            {
                await lease.Process.LoadedAsync(RunLimit, cancellationToken);
                return (started, lease, true);
            }
            catch
            {
                await ReturnAsync(started, lease, goesOn: false);
                throw;
            }
        }

        private PluginWorker Start(PluginTarget plugin)
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
                started = new PluginWorker(plugin, PluginProcess.Start(_executable, plugin, folder, _timeProvider));
            }
            catch
            {
                folder.Remove();
                throw;
            }

            lock (_lock)
            {
                _live[plugin.Id] = started;
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

        /// <summary>Takes back a process that has nothing to do now, its folder emptied; one that does not go on, or whose folder cannot be emptied, is ended.</summary>
        private async Task ReturnAsync(PluginWorker worker, PluginWorker.Lease lease, bool goesOn)
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

        private async Task EndIdleAsync(PluginWorker worker)
        {
            // Used by no run that comes now.
            if (worker.TryRetire())
            {
                await TryEndAsync(worker);
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
                if (_live.GetValueOrDefault(worker.Plugin.Id) == worker)
                {
                    _live.Remove(worker.Plugin.Id);
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

        /// <summary>A run under way over some contents.</summary>
        private sealed record Claim(string Sha256, CancellationTokenSource Stop)
        {
            public TaskCompletionSource Ended { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
