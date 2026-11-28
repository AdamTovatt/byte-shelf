using System.Data.Common;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Migration v0017 on a database made before it, whose derived_size takes no stream.</summary>
    public sealed class DerivedStreamMigrationTests : IAsyncLifetime
    {
        private static readonly string Sha256 = new string('a', 64);

        private TestRoot _old = null!;

        public async Task InitializeAsync()
        {
            _old = TestRoot.CreateBefore("v0017");
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO derived_size (sha256, kind, size_bytes) VALUES (@Sha256, 'thumbnail', 12)", new { Sha256 });
        }

        public async Task DisposeAsync()
        {
            await _old.DisposeAsync();
        }

        [Fact]
        public async Task Before_AStream_IsRefused()
        {
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(connection, "stream"));
        }

        [Fact]
        public async Task Migrate_WhatWasCounted_IsKept()
        {
            DatabaseMigrator.RunMigrations(_old.Root.Database, NullLogger.Instance);
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            Assert.Equal(12, await connection.ExecuteScalarAsync<long>("SELECT size_bytes FROM derived_size WHERE sha256 = @Sha256 AND kind = 'thumbnail'", new { Sha256 }));
        }

        [Fact]
        public async Task Migrate_AStream_IsCountedAndAKindThatIsNone_StillRefused()
        {
            DatabaseMigrator.RunMigrations(_old.Root.Database, NullLogger.Instance);
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            await InsertAsync(connection, "stream");

            Assert.Equal(2, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM derived_size"));
            await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(connection, "hologram"));
        }

        private static Task InsertAsync(DbConnection connection, string kind)
        {
            return connection.ExecuteAsync("INSERT INTO derived_size (sha256, kind, size_bytes) VALUES (@Sha256, @Kind, 34)", new { Sha256, Kind = kind });
        }
    }
}
