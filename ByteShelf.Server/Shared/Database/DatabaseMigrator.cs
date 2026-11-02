using System.Data.Common;
using System.Reflection;
using ByteShelf.Server.Shared.Root;
using DbUp;
using DbUp.Engine;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    public static class DatabaseMigrator
    {
        /// <summary>
        /// Applies the embedded <c>Migrations/*.sql</c> scripts to the database behind
        /// <paramref name="connectionString"/>, each in its own transaction, and puts the database in WAL
        /// mode (a setting SQLite keeps in the file, so once is enough).
        /// </summary>
        public static void RunMigrations(string connectionString, ILogger logger)
        {
            DatabaseTypeHandlers.RegisterAll();

            DatabaseUpgradeResult result = Upgrader(connectionString).PerformUpgrade();

            if (!result.Successful)
            {
                throw new InvalidOperationException($"Error when performing database upgrade, failing on script: {result.ErrorScript?.Name ?? "(null)"} with error {result.Error}");
            }

            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using DbCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }

            logger.LogInformation("Database is up to date.");
        }

        /// <summary>
        /// Opens the root at <paramref name="rootPath"/>, making it when it is missing, and migrates its
        /// database: <c>--migrate-only</c>, and the server's commands that may make a root.
        /// </summary>
        public static RootFolder MigrateRoot(string rootPath, ILogger logger)
        {
            RootFolder root = RootFolder.Open(rootPath);
            RunMigrations(root.ConnectionString, logger);
            return root;
        }

        /// <summary>
        /// Whether every migration this build carries has run on the database behind
        /// <paramref name="connectionString"/>, without changing it: for work beside a running server,
        /// which must not upgrade the database under it.
        /// </summary>
        public static bool IsUpToDate(string connectionString)
        {
            return !Upgrader(connectionString).IsUpgradeRequired();
        }

        private static UpgradeEngine Upgrader(string connectionString)
        {
            return DeployChanges.To
                .SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    (string scriptName) =>
                    {
                        return scriptName.Contains("Migrations") && scriptName.Split('.').Last() == "sql";
                    })
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build();
        }
    }
}
