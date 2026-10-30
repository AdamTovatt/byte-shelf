using System.Data.Common;
using EasyReasy.Database;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Gives each test a fresh root and database of its own, and a fake clock at <see cref="TestInstants.Now"/>.</summary>
    public abstract class RepositoryTestBase : IAsyncLifetime
    {
        protected TestRoot TestRoot { get; private set; } = null!;
        protected DbDataSource DataSource => TestRoot.DataSource;
        protected IDbSessionFactory SessionFactory => TestRoot.SessionFactory;
        protected FakeTimeProvider Clock { get; } = new FakeTimeProvider(TestInstants.Now);

        public virtual Task InitializeAsync()
        {
            TestRoot = TestRoot.Create();
            return Task.CompletedTask;
        }

        public virtual async Task DisposeAsync()
        {
            await TestRoot.DisposeAsync();
        }
    }
}
