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

        /// <summary>Every kind the table took before, each of a size of its own.</summary>
        private static readonly (string Kind, long Bytes)[] Counted = [("thumbnail", 12), ("preview", 34), ("full", 56), ("agent", 78)];

        private TestRoot _old = null!;

        public async Task InitializeAsync()
        {
            _old = TestRoot.CreateBefore("v0017");
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();
            foreach ((string kind, long bytes) in Counted)
            {
                await connection.ExecuteAsync("INSERT INTO derived_size (sha256, kind, size_bytes) VALUES (@Sha256, @Kind, @Bytes)", new { Sha256, Kind = kind, Bytes = bytes });
            }
        }

        public async Task DisposeAsync()
        {
            await _old.DisposeAsync();
        }

        [Fact]
        public async Task Before_AStream_IsRefused()
        {
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            SqliteException refused = await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(connection, "stream"));

            // SQLITE_CONSTRAINT: the table's check of its kinds, not any other failing.
            Assert.Equal(19, refused.SqliteErrorCode);
            Assert.Contains("CHECK constraint failed", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Migrate_WhatWasCounted_IsKept()
        {
            DatabaseMigrator.RunMigrations(_old.Root.Database, NullLogger.Instance);
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            foreach ((string kind, long bytes) in Counted)
            {
                Assert.Equal(bytes, await connection.ExecuteScalarAsync<long>("SELECT size_bytes FROM derived_size WHERE sha256 = @Sha256 AND kind = @Kind", new { Sha256, Kind = kind }));
            }
        }

        [Fact]
        public async Task Migrate_AStream_IsCountedAndAKindThatIsNone_StillRefused()
        {
            DatabaseMigrator.RunMigrations(_old.Root.Database, NullLogger.Instance);
            await using DbConnection connection = await _old.Root.Database.OpenConnectionAsync();

            await InsertAsync(connection, "stream");

            Assert.Equal(Counted.Length + 1, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM derived_size"));
            await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(connection, "hologram"));
        }

        private static Task InsertAsync(DbConnection connection, string kind)
        {
            return connection.ExecuteAsync("INSERT INTO derived_size (sha256, kind, size_bytes) VALUES (@Sha256, @Kind, 34)", new { Sha256, Kind = kind });
        }
    }
}
