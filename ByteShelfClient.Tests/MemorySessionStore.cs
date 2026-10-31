using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelfClient.Tests
{
    /// <summary>A shared session store in memory, whose tokens a test changes as another process would.</summary>
    internal sealed class MemorySessionStore : ISharedSessionStore
    {
        public MemorySessionStore(SessionTokens? tokens)
        {
            Tokens = tokens;
        }

        public SessionTokens? Tokens { get; set; }

        /// <summary>How many times the lock was taken.</summary>
        public int Locks { get; private set; }

        public bool Locked { get; private set; }

        public Task<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
        {
            Locks++;
            Locked = true;
            return Task.FromResult<IAsyncDisposable>(new Release(this));
        }

        public Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Tokens);
        }

        public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken)
        {
            Assert.True(Locked, "Saved without holding the lock.");
            Tokens = tokens;
            return Task.CompletedTask;
        }

        private sealed class Release : IAsyncDisposable
        {
            private readonly MemorySessionStore _store;

            public Release(MemorySessionStore store)
            {
                _store = store;
            }

            public ValueTask DisposeAsync()
            {
                _store.Locked = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
