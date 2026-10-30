using EasyReasy.Database;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Each test runs inside <see cref="Session"/>, a transaction on its own database. Pass
    /// <see cref="Session"/> to every repository call under test.
    /// </summary>
    public abstract class TransactionalRepositoryTestBase : RepositoryTestBase
    {
        protected IDbSession Session { get; private set; } = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Session = await SessionFactory.CreateSessionWithTransactionAsync();
        }

        public override async Task DisposeAsync()
        {
            await Session.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
