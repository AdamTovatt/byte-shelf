using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Tests.Storage
{
    public class MaintenanceGateTests
    {
        [Fact]
        public async Task EnterAsync_WhileHeld_WaitsUntilItIsReleased()
        {
            MaintenanceGate gate = new MaintenanceGate();
            IDisposable first = await gate.EnterAsync(CancellationToken.None);

            Task<IDisposable> second = gate.EnterAsync(CancellationToken.None);
            Assert.False(second.IsCompleted);

            first.Dispose();
            using IDisposable entered = await second.WaitAsync(TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task EnterAsync_CancelledWhileWaiting_ThrowsAndTakesNothing()
        {
            MaintenanceGate gate = new MaintenanceGate();
            IDisposable holder = await gate.EnterAsync(CancellationToken.None);
            using CancellationTokenSource cancel = new CancellationTokenSource();

            Task<IDisposable> waiting = gate.EnterAsync(cancel.Token);
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Task<IDisposable> next = gate.EnterAsync(CancellationToken.None);
            Assert.False(next.IsCompleted);
            holder.Dispose();
            using IDisposable entered = await next.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
