using System.Data.Common;
using EasyReasy.Database;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A session factory that runs what a test says in every transaction, right before it commits, and can make one
    /// chosen transaction fail there, for reaching the code that runs when everything before the commit
    /// succeeded. That one rolls back as a real failed commit would leave it.
    /// </summary>
    public sealed class HookedCommitSessionFactory : IDbSessionFactory
    {
        private readonly DbSessionFactory _inner;
        private int _transactionsUntilFailure;

        public HookedCommitSessionFactory(DbDataSource dataSource)
        {
            _inner = new DbSessionFactory(dataSource);
        }

        /// <summary>Given each transaction about to commit; what it throws keeps it from committing.</summary>
        public Func<IDbSession, Task> BeforeCommit { get; set; } = _ => Task.CompletedTask;

        /// <summary>What a test that has a commit fail expects.</summary>
        public static InvalidOperationException CommitFailed()
        {
            return new InvalidOperationException("The commit failed (test).");
        }

        /// <summary>Makes the <paramref name="number"/>th transaction from now (1 for the next) fail at commit.</summary>
        public void FailTransaction(int number)
        {
            _transactionsUntilFailure = number;
        }

        public Task<IDbSession> CreateSessionAsync()
        {
            return _inner.CreateSessionAsync();
        }

        public async Task<IDbTransactionSession> CreateSessionWithTransactionAsync()
        {
            bool fails = _transactionsUntilFailure > 0 && --_transactionsUntilFailure == 0;
            return new HookedSession(await _inner.CreateSessionWithTransactionAsync(), fails ? _ => throw CommitFailed() : session => BeforeCommit(session));
        }

        private sealed class HookedSession : IDbTransactionSession
        {
            private readonly IDbTransactionSession _inner;
            private readonly Func<IDbSession, Task> _beforeCommit;

            public HookedSession(IDbTransactionSession inner, Func<IDbSession, Task> beforeCommit)
            {
                _inner = inner;
                _beforeCommit = beforeCommit;
            }

            public DbConnection Connection => _inner.Connection;

            public DbTransaction Transaction => _inner.Transaction;

            DbTransaction? IDbSession.Transaction => _inner.Transaction;

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                await _beforeCommit(this);
                await _inner.CommitAsync(cancellationToken);
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                return _inner.RollbackAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return _inner.DisposeAsync();
            }
        }
    }
}
