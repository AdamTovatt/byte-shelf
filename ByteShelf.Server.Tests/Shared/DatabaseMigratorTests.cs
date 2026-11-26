using System.Data.Common;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>What a migration run logs: each script it applied, or that none was needed.</summary>
    public sealed class DatabaseMigratorTests
    {
        [Fact]
        public async Task RunMigrations_OnADatabaseBeforeV0015_LogsEachScriptItAppliesByNameInOrder()
        {
            await using TestRoot old = TestRoot.CreateBefore("v0015");
            IReadOnlyList<string> pending = PendingMigrations(old.Root.Database);
            RecordingLogger logger = new RecordingLogger();

            DatabaseMigrator.RunMigrations(old.Root.Database, logger);

            Assert.Equal("Applied the migration v0015_add_plugin_metadata.", logger.Messages[0]);
            Assert.Equal(pending.Select(name => $"Applied the migration {name}."), logger.Messages);
        }

        [Fact]
        public async Task RunMigrations_OnAnUpToDateDatabase_LogsOnlyThatItIsUpToDate()
        {
            await using TestRoot root = TestRoot.Create();
            RecordingLogger logger = new RecordingLogger();

            DatabaseMigrator.RunMigrations(root.Root.Database, logger);

            Assert.Equal(["Database is up to date."], logger.Messages);
        }

        /// <summary>The scripts before the one that fails stay applied, so the log says so before the run throws.</summary>
        [Fact]
        public async Task RunMigrations_AScriptFails_ThrowsNamingItAfterLoggingThoseAppliedBeforeIt()
        {
            await using TestRoot old = TestRoot.CreateBefore("v0015");
            await using (DbConnection connection = await old.Root.Database.OpenConnectionAsync())
            {
                // v0016 creates this table, so it fails once v0015 has gone in.
                await connection.ExecuteAsync("CREATE TABLE derived_size (sha256 TEXT)");
            }

            RecordingLogger logger = new RecordingLogger();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => DatabaseMigrator.RunMigrations(old.Root.Database, logger));

            Assert.Contains("v0016_add_derived_size", thrown.Message, StringComparison.Ordinal);
            Assert.Equal(["Applied the migration v0015_add_plugin_metadata."], logger.Messages);
        }

        /// <summary>The names of the migrations a run on <paramref name="database"/> would apply, in the order it would.</summary>
        private static IReadOnlyList<string> PendingMigrations(DatabaseFile database)
        {
            using DbConnection connection = database.OpenConnection();
            return DatabaseMigrator.Upgrader(connection).GetScriptsToExecute().Select(script => DatabaseMigrator.MigrationName(script.Name)).ToList();
        }
    }
}
