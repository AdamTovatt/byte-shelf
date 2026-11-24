using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What is being made, once however many ask for it.</summary>
    internal sealed class DerivedMaking
    {
        private readonly Lock _lock = new Lock();
        private readonly Dictionary<JobKey, Job> _jobs = new Dictionary<JobKey, Job>();

        private readonly PluginProcesses _processes;
        private readonly MakerRuns _runs;
        private readonly ILogger<DerivedMaking> _logger;

        public DerivedMaking(PluginProcesses processes, MakerRuns runs, ILogger<DerivedMaking> logger)
        {
            _processes = processes;
            _runs = runs;
            _logger = logger;
        }

        /// <summary>
        /// Has <paramref name="plugin"/> make <paramref name="kind"/> from the contents, with the kinds made in the
        /// same run, unless what it made is there. The answer ends when that is done; it goes on whether or not
        /// anyone waits.
        /// </summary>
        /// <exception cref="PluginUnavailableException">The plugin could not run, whatever the contents.</exception>
        public Task<MakingOutcome> MakeAsync(string sha256, DerivedKind kind, DerivedFilePlugin plugin, PluginPriority priority)
        {
            JobKey key = new JobKey(sha256, plugin.Id, DerivedKinds.GroupOf(kind));
            lock (_lock)
            {
                if (_jobs.TryGetValue(key, out Job? job))
                {
                    job.Turn.RaiseTo(priority);
                    return job.Ended;
                }

                job = new Job(_processes.Enter(plugin.Id, priority));
                _jobs.Add(key, job);
                job.Ended = Task.Run(() => RunAsync(key, plugin, job.Turn), CancellationToken.None);
                return job.Ended;
            }
        }

        private async Task<MakingOutcome> RunAsync(JobKey key, DerivedFilePlugin plugin, PluginTurns.Turn turn)
        {
            try
            {
                MakerRun run = _runs.Of(plugin, key.Sha256, key.Group);
                TakenFiles? taken = await _processes.RunAsync(run, turn, CancellationToken.None);
                return taken == null ? MakingOutcome.NothingToMake : await run.KeepAsync(taken, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return MakingOutcome.Stopped;
            }
            catch (Exception exception)
            {
                // Logged here too, as nobody may be waiting any more.
                _logger.LogError(exception, "The plugin {Plugin} could not be run to make derived files.", plugin.Id);
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _jobs.Remove(key);
                }

                turn.Dispose();
            }
        }

        private sealed record JobKey(string Sha256, string PluginId, MakingGroup Group);

        private sealed class Job
        {
            public Job(PluginTurns.Turn turn)
            {
                Turn = turn;
            }

            public PluginTurns.Turn Turn { get; }

            /// <summary>Ends with the job; set as it is started, under the lock it is found by.</summary>
            public Task<MakingOutcome> Ended { get; set; } = null!;
        }
    }
}
