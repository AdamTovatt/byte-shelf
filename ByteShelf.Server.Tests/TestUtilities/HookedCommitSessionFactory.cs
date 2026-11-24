using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
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
        private readonly Lock _hookLock = new Lock();
        private (string AskedBy, Func<Task> Hook)? _beforeNextTransaction;

        public HookedCommitSessionFactory(DbDataSource dataSource)
        {
            _inner = new DbSessionFactory(dataSource);
        }

        /// <summary>Given each transaction about to commit; what it throws keeps it from committing.</summary>
        public Func<IDbSession, Task> BeforeCommit { get; set; } = _ => Task.CompletedTask;

        /// <summary>
        /// Has <paramref name="hook"/> run once, as the next transaction <paramref name="askedBy"/> asks for is asked for and
        /// before it begins; what it throws is thrown to who asked. <paramref name="askedBy"/> names a method as
        /// <c>Type.Method</c>, which asks if it is on the stack as it asks, so others asking meanwhile pass.
        /// </summary>
        public void BeforeNextTransaction(string askedBy, Func<Task> hook)
        {
            _beforeNextTransaction = (askedBy, hook);
        }

        /// <summary>Ends once <paramref name="askedBy"/> has asked for a transaction, as <see cref="BeforeNextTransaction"/> says, which has yet to begin.</summary>
        public Task NextTransactionAskedAsync(string askedBy)
        {
            TaskCompletionSource asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            BeforeNextTransaction(askedBy, () =>
            {
                asked.TrySetResult();
                return Task.CompletedTask;
            });
            return asked.Task;
        }

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
            if (_beforeNextTransaction is { } beforeNext && IsOnTheStack(beforeNext.AskedBy) && TakeHook(beforeNext))
            {
                await beforeNext.Hook();
            }

            bool fails = _transactionsUntilFailure > 0 && --_transactionsUntilFailure == 0;
            return new HookedSession(await _inner.CreateSessionWithTransactionAsync(), fails ? _ => throw CommitFailed() : session => BeforeCommit(session));
        }

        /// <summary>Whether <paramref name="method"/>, as <c>Type.Method</c>, is on the stack, an async one by its state machine.</summary>
        private static bool IsOnTheStack(string method)
        {
            foreach (StackFrame frame in new StackTrace().GetFrames())
            {
                MethodBase? called = frame.GetMethod();
                if (called?.DeclaringType is not Type type)
                {
                    continue;
                }

                string name = type.Name.StartsWith('<') && type.DeclaringType != null
                    ? $"{type.DeclaringType.Name}.{type.Name[1..type.Name.IndexOf('>')]}"
                    : $"{type.Name}.{called.Name}";
                if (name == method)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TakeHook((string AskedBy, Func<Task> Hook) hook)
        {
            lock (_hookLock)
            {
                if (_beforeNextTransaction != hook)
                {
                    return false;
                }

                _beforeNextTransaction = null;
                return true;
            }
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
