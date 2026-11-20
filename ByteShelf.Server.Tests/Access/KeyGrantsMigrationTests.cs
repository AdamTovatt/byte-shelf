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
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using DbUp;
using EasyReasy.Database;
using Microsoft.Data.Sqlite;
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
        private const string FirstMigrationAfter = "v0014";

        private static readonly Guid Docs = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid Photos = Guid.Parse("10000000-0000-0000-0000-000000000002");
        private static readonly OldKey Writer = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000001"), Docs, "writer", "read_write", ShelfKeyPermission.ReadWrite);
        private static readonly OldKey Backup = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000002"), Photos, "backup", "add_only", ShelfKeyPermission.AddOnly);
        private static readonly OldKey Reader = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000003"), Docs, "reader", "read", ShelfKeyPermission.Read);
        private static readonly OldKey Deleted = new OldKey(Guid.Parse("20000000-0000-0000-0000-000000000004"), Docs, "deleted", "read", ShelfKeyPermission.Read);
        private static readonly Guid LinkId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        private RootFolder _old = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _old = RootFolder.Open(Path.Combine(AppContext.BaseDirectory, "test-roots", "v0013-" + Guid.NewGuid().ToString("N")));
            MigrateThroughV0013(_old.ConnectionString);
            await FillAsBeforeV0014Async(_old.ConnectionString);

            DatabaseMigrator.RunMigrations(_old.ConnectionString, NullLogger.Instance);
        }

        public override async Task DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_old.Path, recursive: true);
            await base.DisposeAsync();
        }

        [Fact]
        public async Task Migrate_EachKey_BecomesOneGrantOnItsShelfUnderTheKeysId()
        {
            IReadOnlyList<ShelfKeyGrant> grants = await ReadGrantsAsync(_old.ConnectionString);

            Assert.Equal(
                new[] { Writer, Backup, Reader }.Select(key => (key.Id, key.Id, key.ShelfId, key.Permission, TestInstants.Now, TestInstants.Now)).OrderBy(grant => grant.Item1),
                grants.Select(grant => (grant.Id, grant.KeyId, grant.ShelfId, grant.Permission, grant.CreatedAt, grant.UpdatedAt)).OrderBy(grant => grant.Item1));
        }

        [Fact]
        public async Task Migrate_KeyTable_LosesItsShelfAndPermissionAndKeepsTheLinkAKeyMade()
        {
            await using SqliteConnection connection = await OpenAsync(_old.ConnectionString);

            Assert.Equal(new[] { "id", "name", "secret_sha256", "created_at" }, await ColumnsAsync(connection, "shelf_key"));
            Assert.Equal(3L, await ScalarAsync(connection, "SELECT COUNT(*) FROM shelf_key"));
            Assert.Equal(Writer.Id.ToString("D"), await ScalarAsync(connection, $"SELECT created_by_key_id FROM shelf_link WHERE id = '{LinkId:D}'"));
        }

        [Fact]
        public async Task Migrate_ChangeLog_HoldsKeysOnlyInTheNewShapeAndLogsEachGrant()
        {
            IReadOnlyList<ChangeLogEntry> entries = await ReadLogAsync(_old.ConnectionString);

            List<ChangeLogEntry> keyStates = entries.Where(entry => entry.EntityType == ChangeEntityType.ShelfKey && entry.Payload != null).ToList();
            Assert.Equal(4, keyStates.Count);
            foreach (ChangeLogEntry key in keyStates)
            {
                using JsonDocument payload = JsonDocument.Parse(key.Payload!);
                Assert.Equal(new[] { "id", "name", "secretSha256", "createdAt" }, payload.RootElement.EnumerateObject().Select(property => property.Name));
                Assert.Equal(key.EntityId, ChangePayload.Deserialize<ShelfKey>(key.Payload!).Id);
            }

            List<ChangeLogEntry> grantEntries = entries.Where(entry => entry.EntityType == ChangeEntityType.ShelfKeyGrant).ToList();
            Assert.All(grantEntries, entry => Assert.Equal(ChangeAction.Added, entry.Action));
            long lastBefore = entries.Where(entry => entry.EntityType != ChangeEntityType.ShelfKeyGrant).Max(entry => entry.Sequence);
            Assert.All(grantEntries, entry => Assert.InRange(entry.Sequence, lastBefore + 1, long.MaxValue));
            Assert.Equal(
                (await ReadGrantsAsync(_old.ConnectionString)).Select(Describe).OrderBy(grant => grant),
                grantEntries.Select(entry => Describe(ChangePayload.Deserialize<ShelfKeyGrant>(entry.Payload!))).OrderBy(grant => grant));
        }

        [Fact]
        public async Task Migrate_WholeCopyInProgress_HoldsItsKeyInTheNewShape()
        {
            await using SqliteConnection connection = await OpenAsync(_old.ConnectionString);

            string payload = (string)(await ScalarAsync(connection, "SELECT payload FROM replication_copy WHERE entity_type = 'shelf_key'"))!;

            Assert.Equal(Writer.Id, ChangePayload.Deserialize<ShelfKey>(payload).Id);
        }

        /// <summary>A replica replaying the primary's migrated log from the start, or a whole copy read from it, gets the same keys and grants.</summary>
        [Fact]
        public async Task Replay_TheMigratedLog_EndsWithTheMigratedKeysAndGrants()
        {
            await ReplayMigratedLogAsync();

            Assert.Equal(
                (await ReadGrantsAsync(_old.ConnectionString)).Select(Describe).OrderBy(grant => grant),
                (await ReadGrantsAsync(TestRoot.Root.ConnectionString)).Select(Describe).OrderBy(grant => grant));
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
            services.AddReplicationServices(AppBaseUrl);
        }

        /// <summary>Applies the migrated database's whole log to this test's fresh one, as a replica following it from the start.</summary>
        private async Task ReplayMigratedLogAsync()
        {
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await Services.GetRequiredService<ReplicaApplier>().ApplyAsync(await ReadLogAsync(_old.ConnectionString), session, new AppliedChanges());
            await session.CommitAsync();
        }

        private static void MigrateThroughV0013(string connectionString)
        {
            DbUp.Engine.DatabaseUpgradeResult result = DeployChanges.To
                .SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(DatabaseMigrator).Assembly,
                    (string name) => name.Contains("Migrations") && name.EndsWith(".sql", StringComparison.Ordinal)
                        && string.CompareOrdinal(name.Split('.')[^2], FirstMigrationAfter) < 0)
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build()
                .PerformUpgrade();

            Assert.True(result.Successful, result.Error?.ToString());
            Assert.Contains(result.Scripts, script => script.Name.Contains("v0013_", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Scripts, script => script.Name.Contains(FirstMigrationAfter, StringComparison.Ordinal));
        }

        /// <summary>Rows and log entries as the server wrote them before v0014.</summary>
        private static async Task FillAsBeforeV0014Async(string connectionString)
        {
            string now = StoredInstant.ToText(TestInstants.Now);
            await using SqliteConnection connection = await OpenAsync(connectionString);

            foreach ((Guid id, string name) in new[] { (Docs, "docs"), (Photos, "photos") })
            {
                await ExecuteAsync(connection, $"INSERT INTO shelf (id, name, quota_bytes, created_at, updated_at) VALUES ('{id:D}', '{name}', NULL, '{now}', '{now}')");
                Shelf shelf = new Shelf { Id = id, Name = name, CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };
                await LogAsync(connection, ChangeEntityType.Shelf, id, ChangeAction.Added, ChangePayload.Serialize(shelf));
            }

            foreach (OldKey key in new[] { Writer, Backup, Reader })
            {
                await ExecuteAsync(
                    connection,
                    $"INSERT INTO shelf_key (id, shelf_id, name, permission, secret_sha256, created_at) VALUES ('{key.Id:D}', '{key.ShelfId:D}', '{key.Name}', '{key.Column}', '{key.SecretSha256}', '{now}')");
            }

            foreach (OldKey key in new[] { Writer, Backup, Deleted, Reader })
            {
                await LogAsync(connection, ChangeEntityType.ShelfKey, key.Id, ChangeAction.Added, key.Payload);
            }

            await LogAsync(connection, ChangeEntityType.ShelfKey, Deleted.Id, ChangeAction.Purged, payload: null);

            await ExecuteAsync(
                connection,
                $"INSERT INTO shelf_link (id, shelf_id, kind, path, overwrite, created_by_key_id, created_at, expires_at) VALUES ('{LinkId:D}', '{Docs:D}', 'upload', '/a.txt', 0, '{Writer.Id:D}', '{now}', '{now}')");

            await ExecuteAsync(
                connection,
                $"INSERT INTO replication_copy (sequence, entity_type, entity_id, action, payload, created_at) VALUES (1, 'shelf_key', '{Writer.Id:D}', 'added', @payload, '{now}')",
                ("payload", Writer.Payload));
        }

        private static async Task LogAsync(SqliteConnection connection, string entityType, Guid entityId, string action, string? payload)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO change_log (entity_type, entity_id, action, payload, created_at) VALUES (@type, @id, @action, @payload, @at)",
                ("type", entityType),
                ("id", entityId.ToString("D")),
                ("action", action),
                ("payload", payload),
                ("at", StoredInstant.ToText(TestInstants.Now)));
        }

        private static async Task<IReadOnlyList<ChangeLogEntry>> ReadLogAsync(string connectionString)
        {
            await using System.Data.Common.DbDataSource dataSource = DataSources.Create(connectionString);
            ChangeLogRepository changeLog = new ChangeLogRepository(dataSource, new DbSessionFactory(dataSource));
            return await changeLog.GetAfterAsync(0, 1000);
        }

        private static async Task<IReadOnlyList<ShelfKeyGrant>> ReadGrantsAsync(string connectionString)
        {
            await using System.Data.Common.DbDataSource dataSource = DataSources.Create(connectionString);
            DbSessionFactory sessions = new DbSessionFactory(dataSource);
            ShelfKeyGrantRepository grants = new ShelfKeyGrantRepository(dataSource, sessions, new ChangeLogRepository(dataSource, sessions));

            List<ShelfKeyGrant> all = new List<ShelfKeyGrant>();
            foreach (OldKey key in new[] { Writer, Backup, Reader, Deleted })
            {
                all.AddRange((await grants.GetForKeyAsync(key.Id)).Select(held => held.Grant));
            }

            return all;
        }

        private static string Describe(ShelfKeyGrant grant)
        {
            return $"{grant.Id:D} {grant.KeyId:D} {grant.ShelfId:D} {grant.Permission} {grant.CreatedAt.UtcTicks} {grant.UpdatedAt.UtcTicks}";
        }

        private static async Task<SqliteConnection> OpenAsync(string connectionString)
        {
            SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue("@" + name, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        private static async Task<IReadOnlyList<string>> ColumnsAsync(SqliteConnection connection, string table)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid";
            List<string> columns = new List<string>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }

        /// <param name="Column">The permission as the old <c>shelf_key.permission</c> held it.</param>
        private sealed record OldKey(Guid Id, Guid ShelfId, string Name, string Column, ShelfKeyPermission Permission)
        {
            public string SecretSha256 => Id.ToString("N").PadRight(64, '0');

            /// <summary>The key as the old <c>ShelfKey</c> model was written into the change log.</summary>
            public string Payload => JsonSerializer.Serialize(
                new { Id, ShelfId, Name, Permission, SecretSha256, CreatedAt = TestInstants.Now },
                JsonDefaults.CamelCase);
        }
    }
}
