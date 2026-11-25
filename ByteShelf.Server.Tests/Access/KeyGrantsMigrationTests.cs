using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// Migration v0014 on a database made before it: keys with a shelf and permission each, a link one of
    /// them made, and change-log and whole-copy entries in the old shape. Afterwards every key holds one
    /// grant on its shelf, the log holds those grants and only the new shape, and replaying the log on a
    /// fresh server ends where the migrated one stands.
    /// </summary>
    public class KeyGrantsMigrationTests : ServiceTestBase
    {
        private static readonly Guid Docs = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid Photos = Guid.Parse("10000000-0000-0000-0000-000000000002");
        private static readonly OldKey Writer = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000001"), Docs, "writer", "read_write", ShelfPermission.ReadWrite);
        private static readonly OldKey Backup = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000002"), Photos, "backup", "add_only", ShelfPermission.AddOnly);
        private static readonly OldKey Reader = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000003"), Docs, "reader", "read", ShelfPermission.Read);
        private static readonly OldKey Deleted = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000004"), Docs, "deleted", "read", ShelfPermission.Read);
        private static readonly Guid LinkId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        /// <summary>The keys the old log holds a state of: the three still there and the one purged since.</summary>
        private static readonly OldKey[] LoggedKeys = { Writer, Backup, Deleted, Reader };

        private RootFolder _old = null!;
        private DateTimeOffset _migrationStarted;
        private DateTimeOffset _migrationEnded;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _old = RootFolder.Open(Path.Combine(AppContext.BaseDirectory, "test-roots", "v0013-" + Guid.NewGuid().ToString("N")));
            TestMigrations.RunBefore("v0014", _old.Database);
            await FillAsBeforeV0014Async(_old.Database);

            _migrationStarted = DateTimeOffset.UtcNow;
            DatabaseMigrator.RunMigrations(_old.Database, NullLogger.Instance);
            _migrationEnded = DateTimeOffset.UtcNow;
        }

        public override async Task DisposeAsync()
        {
            _old.Database.Release();
            Directory.Delete(_old.Path, recursive: true);
            await base.DisposeAsync();
        }

        [Fact]
        public async Task Migrate_EachKey_BecomesOneGrantOnItsShelfUnderTheKeysId()
        {
            IReadOnlyList<ShelfKeyGrant> grants = await ReadGrantsAsync(_old.Database);

            Assert.Equal(
                new[] { Writer, Backup, Reader }.Select(key => (key.Id, key.Id, key.ShelfId, key.Permission, TestInstants.Now, TestInstants.Now)).OrderBy(grant => grant.Item1),
                grants.Select(grant => (grant.Id, grant.KeyId, grant.ShelfId, grant.Permission, grant.CreatedAt, grant.UpdatedAt)).OrderBy(grant => grant.Item1));
        }

        [Fact]
        public async Task Migrate_KeyTable_LosesItsShelfAndPermissionAndKeepsTheLinkAKeyMade()
        {
            await using DbConnection connection = await _old.Database.OpenConnectionAsync();

            Assert.Equal(new[] { "id", "name", "secret_sha256", "created_at" }, await connection.QueryAsync<string>("SELECT name FROM pragma_table_info('shelf_key') ORDER BY cid"));
            Assert.Equal(3L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM shelf_key"));
            Assert.Equal(Writer.Id.ToString("D"), await connection.ExecuteScalarAsync<string>($"SELECT created_by_key_id FROM shelf_link WHERE id = '{LinkId:D}'"));
        }

        [Fact]
        public async Task Migrate_ChangeLog_HoldsEveryKeyStateInTheNewShape()
        {
            IReadOnlyList<ChangeLogEntry> entries = await TestMigrations.ReadLogAsync(_old.Database);

            List<ChangeLogEntry> keyStates = entries.Where(entry => entry.EntityType == ChangeEntityType.ShelfKey && entry.Payload != null).ToList();

            Assert.Equal(LoggedKeys.Select(key => key.Id), keyStates.Select(entry => entry.EntityId));
            Assert.All(keyStates, key =>
            {
                using JsonDocument payload = JsonDocument.Parse(key.Payload!);
                Assert.Equal(new[] { "id", "name", "secretSha256", "createdAt" }, payload.RootElement.EnumerateObject().Select(property => property.Name));
                Assert.Equal(key.EntityId, ChangePayload.Deserialize<ShelfKey>(key.Payload!).Id);
            });
        }

        /// <summary>
        /// Each grant is logged as added, after every entry already there and dated when the migration ran,
        /// so a replica neither misses the entries nor looks behind for them.
        /// </summary>
        [Fact]
        public async Task Migrate_ChangeLog_LogsEachGrantAfterTheEntriesThereDatedWhenItRan()
        {
            IReadOnlyList<ChangeLogEntry> entries = await TestMigrations.ReadLogAsync(_old.Database);

            List<ChangeLogEntry> grantEntries = entries.Where(entry => entry.EntityType == ChangeEntityType.ShelfKeyGrant).ToList();
            long lastBefore = entries.Where(entry => entry.EntityType != ChangeEntityType.ShelfKeyGrant).Max(entry => entry.Sequence);

            Assert.Equal(
                (await ReadGrantsAsync(_old.Database)).Select(Describe).OrderBy(grant => grant),
                grantEntries.Select(entry => Describe(ChangePayload.Deserialize<ShelfKeyGrant>(entry.Payload!))).OrderBy(grant => grant));
            Assert.All(grantEntries, entry =>
            {
                Assert.Equal(ChangeAction.Added, entry.Action);
                Assert.InRange(entry.Sequence, lastBefore + 1, long.MaxValue);
                // The clock's text keeps whole milliseconds and less.
                Assert.InRange(entry.CreatedAt, _migrationStarted.AddMilliseconds(-1), _migrationEnded);
            });
        }

        [Fact]
        public async Task Migrate_WholeCopyInProgress_HoldsItsKeyInTheNewShape()
        {
            await using DbConnection connection = await _old.Database.OpenConnectionAsync();

            string payload = (await connection.ExecuteScalarAsync<string>("SELECT payload FROM replication_copy WHERE entity_type = 'shelf_key'"))!;

            Assert.Equal(Writer.Id, ChangePayload.Deserialize<ShelfKey>(payload).Id);
        }

        /// <summary>A replica replaying the primary's migrated log from the start, or a whole copy read from it, gets the same keys and grants.</summary>
        [Fact]
        public async Task Replay_TheMigratedLog_EndsWithTheMigratedKeysAndGrants()
        {
            await ReplayMigratedLogAsync();

            Assert.Equal(
                (await ReadGrantsAsync(_old.Database)).Select(Describe).OrderBy(grant => grant),
                (await ReadGrantsAsync(TestRoot.Root.Database)).Select(Describe).OrderBy(grant => grant));
            Assert.Null(await Services.GetRequiredService<IShelfKeyRepository>().GetByIdAsync(Deleted.Id));
        }

        /// <summary>A migrated key reaches its own shelf with its permission, and no other.</summary>
        [Fact]
        public async Task Replay_TheMigratedLog_KeyReachesOnlyItsShelf()
        {
            await ReplayMigratedLogAsync();

            ShelfCaller backup = ShelfCaller.ForKey((await Services.GetRequiredService<IShelfKeyRepository>().GetByIdAsync(Backup.Id))!);
            ShelfAccess onPhotos = await AccessResolver.ResolveAsync(backup, await ShelfService.GetByIdAsync(Photos));

            Assert.True(onPhotos.Allows(ShelfOperation.Add));
            Assert.False(onPhotos.Allows(ShelfOperation.Write));
            await Assert.ThrowsAsync<NotFoundException>(async () => await AccessResolver.ResolveAsync(backup, await ShelfService.GetByIdAsync(Docs)));
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(new ReplicationOptions { PublicUrl = AppBaseUrl });
        }

        /// <summary>Applies the migrated database's whole log to this test's fresh one, as a replica following it from the start.</summary>
        private async Task ReplayMigratedLogAsync()
        {
            await using ContentsSession session = await Services.CreateContentsSessionAsync();
            await Services.GetRequiredService<ReplicaApplier>().ApplyAsync(await TestMigrations.ReadLogAsync(_old.Database), session, new AppliedChanges());
            await session.CommitAsync();
        }

        /// <summary>Rows and log entries as the server wrote them before v0014.</summary>
        private static async Task FillAsBeforeV0014Async(DatabaseFile database)
        {
            string now = StoredInstant.ToText(TestInstants.Now);
            await using DbConnection connection = await database.OpenConnectionAsync();

            foreach ((Guid id, string name) in new[] { (Docs, "docs"), (Photos, "photos") })
            {
                await connection.ExecuteAsync($"INSERT INTO shelf (id, name, quota_bytes, created_at, updated_at) VALUES ('{id:D}', '{name}', NULL, '{now}', '{now}')");
                Shelf shelf = new Shelf { Id = id, Name = name, CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };
                await LogAsync(connection, ChangeEntityType.Shelf, id, ChangeAction.Added, ChangePayload.Serialize(shelf));
            }

            foreach (OldKey key in new[] { Writer, Backup, Reader })
            {
                await connection.ExecuteAsync(
                    $"INSERT INTO shelf_key (id, shelf_id, name, permission, secret_sha256, created_at) VALUES ('{key.Id:D}', '{key.ShelfId:D}', '{key.Name}', '{key.Column}', '{key.SecretSha256}', '{now}')");
            }

            foreach (OldKey key in LoggedKeys)
            {
                await LogAsync(connection, ChangeEntityType.ShelfKey, key.Id, ChangeAction.Added, key.Payload);
            }

            await LogAsync(connection, ChangeEntityType.ShelfKey, Deleted.Id, ChangeAction.Purged, payload: null);

            await connection.ExecuteAsync(
                $"INSERT INTO shelf_link (id, shelf_id, kind, path, overwrite, created_by_key_id, created_at, expires_at) VALUES ('{LinkId:D}', '{Docs:D}', 'upload', '/a.txt', 0, '{Writer.Id:D}', '{now}', '{now}')");

            await connection.ExecuteAsync(
                $"INSERT INTO replication_copy (sequence, entity_type, entity_id, action, payload, created_at) VALUES (1, 'shelf_key', '{Writer.Id:D}', 'added', @payload, '{now}')",
                new { payload = Writer.Payload });
        }

        private static async Task LogAsync(DbConnection connection, string entityType, Guid entityId, string action, string? payload)
        {
            await connection.ExecuteAsync(
                "INSERT INTO change_log (entity_type, entity_id, action, payload, created_at) VALUES (@type, @id, @action, @payload, @at)",
                new { type = entityType, id = entityId.ToString("D"), action, payload, at = StoredInstant.ToText(TestInstants.Now) });
        }

        private static async Task<IReadOnlyList<ShelfKeyGrant>> ReadGrantsAsync(DatabaseFile database)
        {
            DbSessionFactory sessions = new DbSessionFactory(database);
            ShelfKeyGrantRepository grants = new ShelfKeyGrantRepository(database, sessions, new ChangeLogRepository(database, sessions));

            // Every key the table holds a grant for, not only the keys the test made, so a stray grant shows.
            await using DbConnection connection = await database.OpenConnectionAsync();
            List<ShelfKeyGrant> all = new List<ShelfKeyGrant>();
            foreach (string keyId in await connection.QueryAsync<string>("SELECT DISTINCT key_id FROM shelf_key_grant"))
            {
                all.AddRange((await grants.GetForKeyAsync(Guid.Parse(keyId))).Select(held => held.Grant));
            }

            return all;
        }

        private static string Describe(ShelfKeyGrant grant)
        {
            return $"{grant.Id:D} {grant.KeyId:D} {grant.ShelfId:D} {grant.Permission} {grant.CreatedAt.UtcTicks} {grant.UpdatedAt.UtcTicks}";
        }

        /// <param name="Column">The permission as the old <c>shelf_key.permission</c> held it.</param>
        private sealed record OldKey(Guid Id, Guid ShelfId, string Name, string Column, ShelfPermission Permission)
        {
            public string SecretSha256 => Id.ToString("N").PadRight(64, '0');

            /// <summary>The key as the old <c>ShelfKey</c> model was written into the change log.</summary>
            public string Payload => JsonSerializer.Serialize(
                new { Id, ShelfId, Name, Permission, SecretSha256, CreatedAt = TestInstants.Now },
                JsonDefaults.CamelCase);
        }
    }
}
