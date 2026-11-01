using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Jobs
{
    /// <remarks>
    /// The fake clock is advanced until a pass has begun, and the worker may get one more pass in before
    /// it stops, so the tests check the first passes and their spacing rather than how many there are.
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

            IReadOnlyList<DateTimeOffset> starts = worker.PassStarts;
            Assert.Equal(TestInstants.Now, starts[0]);
            Assert.True(starts[1] >= starts[0] + CountingWorker.TestInterval);
        }

        [Fact]
        public async Task ExecuteAsync_PassFails_RunsAgainOnSchedule()
        {
            CountingWorker worker = new CountingWorker(Services.GetRequiredService<IServiceScopeFactory>(), Clock) { FailFirstPass = true };

            await worker.StartAsync(CancellationToken.None);
            await worker.WaitForPassAsync(1);
            await Clock.AdvanceUntilAsync(worker.WaitForPassAsync(2), CountingWorker.TestInterval);
            await worker.StopAsync(CancellationToken.None);

            IReadOnlyList<DateTimeOffset> starts = worker.PassStarts;
            Assert.True(starts[1] >= starts[0] + CountingWorker.TestInterval);
        }

        private sealed class CountingWorker : PeriodicJobWorker<int>
        {
            public static readonly TimeSpan TestInterval = TimeSpan.FromMinutes(5);

            private readonly object _gate = new object();
            private readonly TimeProvider _clock;
            private readonly List<DateTimeOffset> _passStarts = new List<DateTimeOffset>();
            private readonly List<(int Pass, TaskCompletionSource Signal)> _waiters = new List<(int, TaskCompletionSource)>();

            public CountingWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
                : base(scopeFactory, timeProvider, NullLogger.Instance)
            {
                _clock = timeProvider;
            }

            public bool FailFirstPass { get; init; }

            /// <summary>When each pass began, first first.</summary>
            public IReadOnlyList<DateTimeOffset> PassStarts
            {
                get
                {
                    lock (_gate)
                    {
                        return _passStarts.ToList();
                    }
                }
            }

            protected override string JobName => "test.counting";

            protected override TimeSpan Interval => TestInterval;

            public Task WaitForPassAsync(int pass)
            {
                lock (_gate)
                {
                    if (_passStarts.Count >= pass)
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
                    _passStarts.Add(_clock.GetUtcNow());
                    pass = _passStarts.Count;
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
