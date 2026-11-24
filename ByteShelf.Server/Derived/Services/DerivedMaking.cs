using ByteShelf.Server.Derived.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What is being made: once however many ask for it, and one run at a time per plugin.</summary>
    internal sealed class DerivedMaking : IAsyncDisposable
    {
        private readonly Lock _lock = new Lock();
        private readonly Dictionary<JobKey, Job> _jobs = new Dictionary<JobKey, Job>();
        private readonly Dictionary<string, MakingTurns> _turns = new Dictionary<string, MakingTurns>(StringComparer.Ordinal);
        private bool _stopped;

        private readonly MakerRuns _runs;
        private readonly ILogger<DerivedMaking> _logger;

        public DerivedMaking(MakerRuns runs, ILogger<DerivedMaking> logger)
        {
            _runs = runs;
            _logger = logger;
        }

        /// <summary>
        /// Has <paramref name="plugin"/> make <paramref name="kind"/> from the contents, with the kinds made in the
        /// same run, unless what it made is there. The answer ends when that is done; it goes on whether or not
        /// anyone waits.
        /// </summary>
        /// <exception cref="MakerUnavailableException">The plugin could not run, whatever the contents.</exception>
        public Task<MakingOutcome> MakeAsync(string sha256, DerivedKind kind, DerivedFilePlugin plugin, MakingPriority priority)
        {
            JobKey key = new JobKey(sha256, plugin.Id, DerivedKinds.GroupOf(kind));
            lock (_lock)
            {
                if (_stopped)
                {
                    return Task.FromResult(MakingOutcome.Stopped);
                }

                if (_jobs.TryGetValue(key, out Job? job))
                {
                    job.Turn.RaiseTo(priority);
                    return job.Ended;
                }

                if (!_turns.TryGetValue(plugin.Id, out MakingTurns? turns))
                {
                    turns = new MakingTurns();
                    _turns.Add(plugin.Id, turns);
                }

                job = new Job(key, plugin, turns.Enter(priority));
                _jobs.Add(key, job);
                job.Ended = Task.Run(() => RunAsync(job), CancellationToken.None);
                return job.Ended;
            }
        }

        /// <summary>
        /// Stops everything being made from the contents, queued or running: once this ends no process reads
        /// them and nothing one wrote is left, now or by an earlier run. Hold the contents' lock, so nothing
        /// more starts.
        /// </summary>
        /// <exception cref="MakerNotStoppedException">A process is still there, or what it wrote.</exception>
        public async Task StopAsync(string sha256)
        {
            List<Job> jobs;
            lock (_lock)
            {
                jobs = _jobs.Values.Where(job => job.Key.Sha256 == sha256).ToList();
            }

            await StopAsync(jobs);
            await _runs.RemoveLeftAsync(sha256);
        }

        /// <summary>Stops everything being made, and has nothing more be.</summary>
        public async Task StopAllAsync()
        {
            List<Job> jobs;
            lock (_lock)
            {
                _stopped = true;
                jobs = _jobs.Values.ToList();
            }

            await StopAsync(jobs);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAllAsync();
        }

        private static async Task StopAsync(IReadOnlyList<Job> jobs)
        {
            foreach (Job job in jobs)
            {
                await job.Stop.CancelAsync();
            }

            foreach (Job job in jobs)
            {
                // How it ended is its own callers' to know.
                Task ended = job.Ended;
                await ended.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        private async Task<MakingOutcome> RunAsync(Job job)
        {
            try
            {
                await job.Turn.Granted.WaitAsync(job.Stop.Token);
                return await _runs.RunAsync(job.Key.Sha256, job.Key.Group, job.Plugin, job.Stop.Token);
            }
            catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
            {
                return MakingOutcome.Stopped;
            }
            catch (Exception exception)
            {
                // Logged here too, as nobody may be waiting any more.
                _logger.LogError(exception, "The plugin {Plugin} could not be run to make derived files.", job.Plugin.Id);
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _jobs.Remove(job.Key);
                }

                job.Turn.Dispose();
            }
        }

        private sealed record JobKey(string Sha256, string PluginId, MakingGroup Group);

        private sealed class Job
        {
            public Job(JobKey key, DerivedFilePlugin plugin, MakingTurns.Turn turn)
            {
                Key = key;
                Plugin = plugin;
                Turn = turn;
            }

            public JobKey Key { get; }

            public DerivedFilePlugin Plugin { get; }

            public MakingTurns.Turn Turn { get; }

            // Not disposed: with no timer and no linked token it holds nothing, and it may be cancelled as the job ends.
            public CancellationTokenSource Stop { get; } = new CancellationTokenSource();

            /// <summary>Ends with the job; set as it is started, under the lock it is found by.</summary>
            public Task<MakingOutcome> Ended { get; set; } = null!;
        }
    }
}
