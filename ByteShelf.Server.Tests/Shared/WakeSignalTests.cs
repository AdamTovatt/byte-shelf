using ByteShelf.Server.Shared.Concurrency;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>A wait between rounds that a wake ends early.</summary>
    public sealed class WakeSignalTests : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

        private readonly WakeSignal _signal = new WakeSignal();
        private readonly FakeTimeProvider _clock = new FakeTimeProvider(TestInstants.Now);

        [Fact]
        public async Task WaitAsync_WokenWhileWaiting_ReturnsBeforeTheTimeout()
        {
            Task wait = _signal.WaitAsync(Timeout, _clock, CancellationToken.None);

            _signal.Wake();

            await wait.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task WaitAsync_WokenBeforeAnyoneWaited_ReturnsAtOnce()
        {
            _signal.Wake();

            await _signal.WaitAsync(Timeout, _clock, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task WaitAsync_WokenTwiceBeforeAnyoneWaited_WakesOnlyTheNextWait()
        {
            _signal.Wake();
            _signal.Wake();
            await _signal.WaitAsync(Timeout, _clock, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Task second = _signal.WaitAsync(Timeout, _clock, CancellationToken.None);
            await Task.Delay(50);

            Assert.False(second.IsCompleted);
            await _clock.AdvanceUntilAsync(second, Timeout);
        }

        [Fact]
        public async Task WaitAsync_NotWoken_ReturnsOnceTheTimeoutPasses()
        {
            Task wait = _signal.WaitAsync(Timeout, _clock, CancellationToken.None);

            _clock.Advance(Timeout - TimeSpan.FromSeconds(1));
            await Task.Delay(50);
            Assert.False(wait.IsCompleted);

            await _clock.AdvanceUntilAsync(wait, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task WaitAsync_Cancelled_Throws()
        {
            using CancellationTokenSource cancel = new CancellationTokenSource();
            Task wait = _signal.WaitAsync(Timeout, _clock, cancel.Token);

            await cancel.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        }

        public void Dispose()
        {
            _signal.Dispose();
        }
    }
}
