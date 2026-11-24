using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>
    /// Migration v0015 on a database made before it, whose reading of a photo has no reader's version, nor do its
    /// change-log entries or the whole copy it holds. Afterwards all of them are version 1's, and replaying the log
    /// on a fresh server ends where the migrated one stands.
    /// </summary>
    public class ReaderVersionMigrationTests : ServiceTestBase
    {
        private static readonly string Photo = new string('a', 64);
        private static readonly string Removed = new string('b', 64);

        private RootFolder _old = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _old = RootFolder.Open(Path.Combine(AppContext.BaseDirectory, "test-roots", "v0014-" + Guid.NewGuid().ToString("N")));
            TestMigrations.RunBefore("v0015", _old.Database);
            await FillAsBeforeV0015Async();
            DatabaseMigrator.RunMigrations(_old.Database, NullLogger.Instance);
        }

        public override async Task DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_old.Path, recursive: true);
            await base.DisposeAsync();
        }

        [Fact]
        public async Task Migrate_Row_IsVersionOnes()
        {
            await using System.Data.Common.DbDataSource dataSource = _old.Database.CreateDataSource();
            DbSessionFactory sessions = new DbSessionFactory(dataSource);

            ObjectMetadata? row = await new ObjectMetadataRepository(dataSource, sessions, new ChangeLogRepository(dataSource, sessions)).GetBySha256Async(Photo);

            Assert.Equal((1, "Nikon"), (row!.ReaderVersion, row.Metadata.Camera));
        }

        [Fact]
        public async Task Migrate_ChangeLog_HoldsEveryReadingWithItsVersionAndWhatElseItHeldAsItWas()
        {
            IReadOnlyList<ChangeLogEntry> entries = await ReadLogAsync();

            Assert.Equal([ChangeEntityType.Shelf, ChangeEntityType.ObjectMetadata, ChangeEntityType.ObjectMetadata, ChangeEntityType.ObjectMetadata], entries.Select(entry => entry.EntityType));
            Assert.Equal(ShelfPayload(), entries[0].Payload);
            Assert.All(entries.Skip(1).Take(2), entry => AssertVersionOnes(entry.Payload!));
            Assert.Null(entries[3].Payload);
        }

        [Fact]
        public async Task Migrate_WholeCopyInProgress_HoldsItsReadingWithItsVersion()
        {
            await using SqliteConnection connection = await _old.Database.OpenConnectionAsync();

            Assert.Equal(ShelfPayload(), await connection.ExecuteScalarAsync<string>("SELECT payload FROM replication_copy WHERE entity_type = 'shelf'"));
            AssertVersionOnes((await connection.ExecuteScalarAsync<string>("SELECT payload FROM replication_copy WHERE entity_type = 'object_metadata'"))!);
        }

        /// <summary>As a replica following the migrated primary from the start does, or one that resumes its whole copy.</summary>
        [Fact]
        public async Task Replay_TheMigratedLog_EndsWithTheMigratedReading()
        {
            await using (IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync())
            {
                await Services.GetRequiredService<ReplicaApplier>().ApplyAsync(await ReadLogAsync(), session, new AppliedChanges());
                await session.CommitAsync();
            }

            IObjectMetadataRepository rows = Services.GetRequiredService<IObjectMetadataRepository>();
            ObjectMetadata? row = await rows.GetBySha256Async(Photo);
            Assert.Equal((1, "Nikon"), (row!.ReaderVersion, row.Metadata.Camera));
            Assert.Null(await rows.GetBySha256Async(Removed));
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(new ReplicationOptions { PublicUrl = AppBaseUrl });
        }

        private static void AssertVersionOnes(string payload)
        {
            ObjectMetadata reading = ChangePayload.Deserialize<ObjectMetadata>(payload);
            Assert.Equal((1, TestInstants.Now, "Nikon"), (reading.ReaderVersion, reading.ReadAt, reading.Metadata.Camera));
        }

        /// <summary>A row, log entries and a whole copy in progress as the server wrote them before v0015.</summary>
        private async Task FillAsBeforeV0015Async()
        {
            await using SqliteConnection connection = await _old.Database.OpenConnectionAsync();
            string now = StoredInstant.ToText(TestInstants.Now);
            await connection.ExecuteAsync(
                "INSERT INTO object_metadata (id, sha256, read_at, camera, camera_fold) VALUES (@Id, @Photo, @now, 'Nikon', 'nikon')",
                new { Id = ObjectMetadata.IdFor(Photo).ToString("D"), Photo, now });

            (string Type, Guid Id, string Action, string? Payload)[] entries =
            [
                (ChangeEntityType.Shelf, Guid.Parse("10000000-0000-0000-0000-000000000001"), ChangeAction.Added, ShelfPayload()),
                (ChangeEntityType.ObjectMetadata, ObjectMetadata.IdFor(Photo), ChangeAction.Added, OldPayload(Photo)),
                (ChangeEntityType.ObjectMetadata, ObjectMetadata.IdFor(Removed), ChangeAction.Added, OldPayload(Removed)),
                (ChangeEntityType.ObjectMetadata, ObjectMetadata.IdFor(Removed), ChangeAction.Purged, null),
            ];
            foreach ((string type, Guid id, string action, string? payload) in entries)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO change_log (entity_type, entity_id, action, payload, created_at) VALUES (@type, @id, @action, @payload, @now)",
                    new { type, id = id.ToString("D"), action, payload, now });
            }

            foreach ((string type, Guid id, string action, string? payload) in entries.Take(2))
            {
                await connection.ExecuteAsync(
                    "INSERT INTO replication_copy (sequence, entity_type, entity_id, action, payload, created_at) VALUES ((SELECT count(*) + 1 FROM replication_copy), @type, @id, @action, @payload, @now)",
                    new { type, id = id.ToString("D"), action, payload, now });
            }
        }

        private static string ShelfPayload()
        {
            return ChangePayload.Serialize(new Shelf { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "docs", CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now });
        }

        /// <summary>A reading of a photo as the model was written into the log before it had its reader's version.</summary>
        private static string OldPayload(string sha256)
        {
            return ChangePayload.Serialize(new { Id = ObjectMetadata.IdFor(sha256), Sha256 = sha256, ReadAt = TestInstants.Now, Metadata = new ContentMetadata { Camera = "Nikon" } });
        }

        private async Task<IReadOnlyList<ChangeLogEntry>> ReadLogAsync()
        {
            await using System.Data.Common.DbDataSource dataSource = _old.Database.CreateDataSource();
            return await new ChangeLogRepository(dataSource, new DbSessionFactory(dataSource)).GetAfterAsync(0, 1000);
        }
    }
}
