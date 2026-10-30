using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Jobs
{
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

            Assert.Equal(2, await CountRecordedRunsAsync(succeeded: true));
        }

        [Fact]
        public async Task ExecuteAsync_PassFails_RecordsTheFailureAndRunsAgainOnSchedule()
        {
            CountingWorker worker = new CountingWorker(Services.GetRequiredService<IServiceScopeFactory>(), Clock) { FailFirstPass = true };

            await worker.StartAsync(CancellationToken.None);
            await worker.WaitForPassAsync(1);
            await Clock.AdvanceUntilAsync(worker.WaitForPassAsync(2), CountingWorker.TestInterval);
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountRecordedRunsAsync(succeeded: false));
            Assert.Equal(1, await CountRecordedRunsAsync(succeeded: true));
        }

        private async Task<int> CountRecordedRunsAsync(bool succeeded)
        {
            await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
            return await session.Connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM job_run WHERE job_name = @jobName AND success = @succeeded",
                new { jobName = CountingWorker.TestJobName, succeeded });
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
