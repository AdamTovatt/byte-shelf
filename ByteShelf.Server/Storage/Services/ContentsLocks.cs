using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// A lock on what the server has of one contents beside the files that hold them (<see cref="IContentsTraces"/>):
    /// what plugins read and make from them. Whether there may be any (a plain file holds the contents) is for the
    /// database to say, so whoever starts reading, keeps what was made or removes it holds the lock from that question
    /// to the answer's commit.
    /// </summary>
    /// <remarks>
    /// Only a swap, which holds the database's write lock too, waits for a second one while it holds one, and a
    /// holder may wait for what plugins do with those contents to end, which waits for no other contents: so no
    /// two wait for each other.
    /// </remarks>
    public sealed class ContentsLocks
    {
        /// <summary>For tests that need a race to happen in a set order.</summary>
        internal KeyedAsyncLock Locks { get; } = new KeyedAsyncLock();

        /// <summary>Holds the lock; dispose the answer to release it.</summary>
        public Task<IDisposable> LockAsync(string sha256, CancellationToken cancellationToken = default)
        {
            return Locks.AcquireAsync(sha256, cancellationToken);
        }

        /// <summary>Holds the lock if nobody holds or waits for it; null when somebody does.</summary>
        public IDisposable? TryLock(string sha256)
        {
            return Locks.TryAcquire(sha256);
        }
    }
}
