using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Tests.Shared
{
    public class KeyedAsyncLockTests
    {
        [Fact]
        public async Task AcquireAsync_SameKeyHeld_WaitsForRelease()
        {
            KeyedAsyncLock locks = new KeyedAsyncLock();
            IDisposable first = await locks.AcquireAsync("a");

            Task<IDisposable> second = locks.AcquireAsync("a");
            await Task.Delay(20);
            Assert.False(second.IsCompleted);

            first.Dispose();
            (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }

        [Fact]
        public async Task AcquireAsync_OtherKeyHeld_DoesNotWait()
        {
            KeyedAsyncLock locks = new KeyedAsyncLock();
            using IDisposable held = await locks.AcquireAsync("a");

            Task<IDisposable> other = locks.AcquireAsync("b");

            Assert.True(other.IsCompletedSuccessfully);
            (await other).Dispose();
        }

        [Fact]
        public async Task Dispose_LastHolder_ForgetsTheKey()
        {
            KeyedAsyncLock locks = new KeyedAsyncLock();

            (await locks.AcquireAsync("a")).Dispose();

            Assert.Equal(0, locks.ActiveKeyCount);
        }

        [Fact]
        public async Task AcquireAsync_WaiterCancelled_LeavesTheLockUsable()
        {
            KeyedAsyncLock locks = new KeyedAsyncLock();
            IDisposable held = await locks.AcquireAsync("a");
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            Task<IDisposable> waiter = locks.AcquireAsync("a", cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            held.Dispose();

            Assert.Equal(0, locks.ActiveKeyCount);
            (await locks.AcquireAsync("a").WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }

        [Fact]
        public async Task Dispose_Twice_ReleasesOnce()
        {
            KeyedAsyncLock locks = new KeyedAsyncLock();
            IDisposable first = await locks.AcquireAsync("a");
            first.Dispose();
            IDisposable second = await locks.AcquireAsync("a");

            first.Dispose();
            Task<IDisposable> third = locks.AcquireAsync("a");
            await Task.Delay(20);

            Assert.False(third.IsCompleted);
            second.Dispose();
            (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }
    }
}
