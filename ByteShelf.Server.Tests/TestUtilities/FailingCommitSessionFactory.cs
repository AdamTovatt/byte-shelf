using System.Data.Common;
using EasyReasy.Database;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A session factory that can make one chosen transaction fail at commit, for reaching the code
    /// that runs when everything before the commit succeeded. The transaction rolls back as a real
    /// failed commit would leave it.
    /// </summary>
    public sealed class FailingCommitSessionFactory : IDbSessionFactory
    {
        private readonly DbSessionFactory _inner;
        private int _transactionsUntilFailure;

        public FailingCommitSessionFactory(DbDataSource dataSource)
        {
            _inner = new DbSessionFactory(dataSource);
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
            IDbTransactionSession session = await _inner.CreateSessionWithTransactionAsync();
            if (_transactionsUntilFailure > 0 && --_transactionsUntilFailure == 0)
            {
                return new FailingSession(session);
            }

            return session;
        }

        private sealed class FailingSession : IDbTransactionSession
        {
            private readonly IDbTransactionSession _inner;

            public FailingSession(IDbTransactionSession inner)
            {
                _inner = inner;
            }

            public DbConnection Connection => _inner.Connection;

            public DbTransaction Transaction => _inner.Transaction;

            DbTransaction? IDbSession.Transaction => _inner.Transaction;

            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("The commit failed (test).");
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
