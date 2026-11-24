using EasyReasy.Database;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Writes over what plugins have of one contents in a transaction begun before the contents' lock is taken, as a swap
    /// of contents does: a swap holds the database while it waits for that lock, so whoever held the lock while waiting
    /// for the database would wait for the swap as it waits for them.
    /// </summary>
    public sealed class ContentsTransactions
    {
        private readonly IDbSessionFactory _sessions;
        private readonly ContentsLocks _locks;

        public ContentsTransactions(IDbSessionFactory sessions, ContentsLocks locks)
        {
            _sessions = sessions;
            _locks = locks;
        }

        /// <summary>Runs <paramref name="write"/> under the contents' lock and commits what it wrote, unless it throws.</summary>
        public async Task<T> RunAsync<T>(string sha256, Func<IDbTransactionSession, Task<T>> write, CancellationToken cancellationToken = default)
        {
            await using IDbTransactionSession session = await _sessions.CreateSessionWithTransactionAsync();
            using (await _locks.LockAsync(sha256, cancellationToken))
            {
                T result = await write(session);
                await session.CommitAsync(CancellationToken.None);
                return result;
            }
        }

        /// <summary>
        /// As <see cref="RunAsync"/>, unless somebody holds or waits for the lock, as it is in use: then it writes nothing
        /// and answers false, waiting for nobody.
        /// </summary>
        public async Task<bool> TryRunAsync(string sha256, Func<IDbTransactionSession, Task> write)
        {
            await using IDbTransactionSession session = await _sessions.CreateSessionWithTransactionAsync();
            using IDisposable? held = _locks.TryLock(sha256);
            if (held == null)
            {
                return false;
            }

            await write(session);
            await session.CommitAsync(CancellationToken.None);
            return true;
        }
    }
}
