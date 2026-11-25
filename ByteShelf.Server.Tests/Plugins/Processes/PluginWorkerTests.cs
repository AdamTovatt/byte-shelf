using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>Who may use a worker, and when it is used no more.</summary>
    public sealed class PluginWorkerTests : IAsyncLifetime
    {
        private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(1);

        private readonly TestRoot _root = TestRoot.Create();
        private readonly TestClock _clock = new TestClock(TestInstants.Now);
        private PluginWorker _worker = null!;
        private int _retired;

        public Task InitializeAsync()
        {
            PluginTarget plugin = new PluginTarget("test", string.Empty);
            _worker = new PluginWorker(plugin, PluginProcess.Start(ShellProgram.Of("exec sleep 1000"), plugin, WorkFolder.Create(_root.Root), TimeProvider.System));
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _worker.EndAsync();
            await _root.DisposeAsync();
        }

        [UnixFact]
        public void TryUse_OneInUse_IsNotUsed()
        {
            using PluginWorker.Lease? lease = _worker.TryUse("a");
            Assert.NotNull(lease);

            using PluginWorker.Lease? other = _worker.TryUse("b");

            Assert.Null(other);
            Assert.False(_worker.HasRead("b"));
        }

        [UnixFact]
        public void Rest_UnusedForTheLimit_IsUsedNoMoreAndRetired()
        {
            _worker.Rest(_clock, IdleLimit, () => _retired++);

            _clock.Advance(IdleLimit);

            Assert.Equal(1, _retired);
            using PluginWorker.Lease? lease = _worker.TryUse("a");
            Assert.Null(lease);
        }

        [UnixFact]
        public void Rest_UsedBeforeTheLimit_GoesOn()
        {
            _worker.Rest(_clock, IdleLimit, () => _retired++);
            using (_worker.TryUse("a"))
            {
                _clock.Advance(IdleLimit);
            }

            Assert.Equal(0, _retired);
            using PluginWorker.Lease? lease = _worker.TryUse("b");
            Assert.NotNull(lease);
        }

        [UnixFact]
        public void Rest_AgainAfterAUse_WaitsTheWholeLimitAnew()
        {
            _worker.Rest(_clock, IdleLimit, () => _retired++);
            _clock.Advance(IdleLimit / 2);
            using (_worker.TryUse("a"))
            {
            }

            _worker.Rest(_clock, IdleLimit, () => _retired++);
            _clock.Advance(IdleLimit / 2);
            Assert.Equal(0, _retired);

            _clock.Advance(IdleLimit / 2);
            Assert.Equal(1, _retired);
        }
    }
}
