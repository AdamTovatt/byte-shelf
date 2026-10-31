using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Jobs
{
    /// <remarks>
    /// The fake clock is advanced until a pass has begun, and the worker may get one more pass in before
    /// it stops, so the tests check the first recorded runs and their spacing rather than how many there
    /// are.
    /// </remarks>
    public class PeriodicJobWorkerTests : ServiceTestBase
    {
        [Fact]
        public async Task ExecuteAsync_Started_RunsAtOnceAndThenEveryInterval()
        {
            CountingWorker worker = new CountingWorker(Services.GetRequiredService<IServiceScopeFactory>(), Clock);

            await worker.StartAsync(CancellationToken.None);
            await worker.WaitForPassAsync(1);
            await Clock.AdvanceUntilAsync(worker.WaitForPassAsync(2), CountingWorker.TestInterval);
            await worker.StopAsync(CancellationToken.None);

            IReadOnlyList<RecordedRun> runs = await GetRecordedRunsAsync();
            Assert.Equal((TestInstants.Now, true), (runs[0].StartedAt, runs[0].Success));
            Assert.True(runs[1].Success);
            Assert.True(runs[1].StartedAt >= runs[0].StartedAt + CountingWorker.TestInterval);
        }

        [Fact]
        public async Task ExecuteAsync_PassFails_RecordsTheFailureAndRunsAgainOnSchedule()
        {
            CountingWorker worker = new CountingWorker(Services.GetRequiredService<IServiceScopeFactory>(), Clock) { FailFirstPass = true };

            await worker.StartAsync(CancellationToken.None);
            await worker.WaitForPassAsync(1);
            await Clock.AdvanceUntilAsync(worker.WaitForPassAsync(2), CountingWorker.TestInterval);
            await worker.StopAsync(CancellationToken.None);

            IReadOnlyList<RecordedRun> runs = await GetRecordedRunsAsync();
            Assert.Equal((false, true), (runs[0].Success, runs[1].Success));
            Assert.True(runs[1].StartedAt >= runs[0].StartedAt + CountingWorker.TestInterval);
        }

        /// <summary>The worker's recorded runs, first started first.</summary>
        private async Task<IReadOnlyList<RecordedRun>> GetRecordedRunsAsync()
        {
            await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
            IEnumerable<RecordedRun> runs = await session.Connection.QueryAsync<RecordedRun>(
                $@"SELECT started_at AS {nameof(RecordedRun.StartedAt)}, success AS {nameof(RecordedRun.Success)}
                   FROM job_run WHERE job_name = @jobName ORDER BY started_at",
                new { jobName = CountingWorker.TestJobName });
            return runs.ToList();
        }

        private sealed class RecordedRun
        {
            public DateTimeOffset StartedAt { get; set; }
            public bool Success { get; set; }
        }

        private sealed class CountingWorker : PeriodicJobWorker<int>
        {
            public const string TestJobName = "test.counting";
            public static readonly TimeSpan TestInterval = TimeSpan.FromMinutes(5);

            private readonly object _gate = new object();
            private readonly List<(int Pass, TaskCompletionSource Signal)> _waiters = new List<(int, TaskCompletionSource)>();
            private int _passes;

            public CountingWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
                : base(scopeFactory, timeProvider, NullLogger.Instance)
            {
            }

            public bool FailFirstPass { get; init; }

            protected override string JobName => TestJobName;

            protected override TimeSpan Interval => TestInterval;

            public Task WaitForPassAsync(int pass)
            {
                lock (_gate)
                {
                    if (_passes >= pass)
                    {
                        return Task.CompletedTask;
                    }

                    TaskCompletionSource signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Add((pass, signal));
                    return signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }

            protected override Task<int> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
            {
                int pass;
                lock (_gate)
                {
                    pass = ++_passes;
                    foreach ((int waitedPass, TaskCompletionSource signal) in _waiters.Where(waiter => waiter.Pass <= pass).ToList())
                    {
                        signal.TrySetResult();
                    }
                }

                if (FailFirstPass && pass == 1)
                {
                    throw new InvalidOperationException("The first pass fails (test).");
                }

                return Task.FromResult(pass);
            }
        }
    }
}
