using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A clock that stands still until a test moves it, and tells when something waits on it, so a test can
    /// move it to just before or at the end of one wait whatever the machine's load.
    /// </summary>
    public sealed class TestClock : FakeTimeProvider
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        private readonly List<WatchedTimer> _timers = new List<WatchedTimer>();

        public TestClock(DateTimeOffset now)
            : base(now)
        {
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            WatchedTimer timer = new WatchedTimer(this, dueTime, base.CreateTimer(callback, state, dueTime, period));
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        /// <summary>Waits until something waits on the clock for exactly <paramref name="dueTime"/>.</summary>
        public async Task WaitForTimerAsync(TimeSpan dueTime)
        {
            using CancellationTokenSource limit = new CancellationTokenSource(RealTimeLimit);
            while (!IsWaitedOnFor(dueTime))
            {
                if (limit.IsCancellationRequested)
                {
                    throw new TimeoutException($"Nothing waits on the clock for {dueTime}.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
            }
        }

        private bool IsWaitedOnFor(TimeSpan dueTime)
        {
            lock (_timers)
            {
                return _timers.Exists(timer => timer.DueTime == dueTime);
            }
        }

        private void Remove(WatchedTimer timer)
        {
            lock (_timers)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class WatchedTimer : ITimer
        {
            private readonly TestClock _clock;
            private readonly ITimer _inner;

            public WatchedTimer(TestClock clock, TimeSpan dueTime, ITimer inner)
            {
                _clock = clock;
                _inner = inner;
                DueTime = dueTime;
            }

            public TimeSpan DueTime { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTime = dueTime;
                return _inner.Change(dueTime, period);
            }

            public void Dispose()
            {
                _clock.Remove(this);
                _inner.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                _clock.Remove(this);
                await _inner.DisposeAsync();
            }
        }
    }
}
