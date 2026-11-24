using System.Data.Common;
using System.Reflection;
using ByteShelf.Server.Shared.Root;
using DbUp;
using DbUp.Engine;
using DbUp.Sqlite.Helpers;

namespace ByteShelf.Server.Shared.Database
{
    public static class DatabaseMigrator
    {
        /// <summary>
        /// Applies the embedded <c>Migrations/*.sql</c> scripts to <paramref name="database"/>,
        /// each in its own transaction, and puts it in WAL mode (a setting SQLite keeps in the file, so once
        /// is enough).
        /// </summary>
        public static void RunMigrations(DatabaseFile database, ILogger logger)
        {
            using DbConnection connection = database.OpenConnection();

            DatabaseUpgradeResult result = Upgrader(connection).PerformUpgrade();

            if (!result.Successful)
            {
                throw new InvalidOperationException($"Error when performing database upgrade, failing on script: {result.ErrorScript?.Name ?? "(null)"} with error {result.Error}");
            }

            using (DbCommand command = connection.CreateCommand())
            {
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
            RunMigrations(root.Database, logger);
            return root;
        }

        /// <summary>
        /// Whether every migration this build carries has run on <paramref name="database"/>, without changing
        /// it: for work beside a running server, which must not upgrade the database under it.
        /// </summary>
        public static bool IsUpToDate(DatabaseFile database)
        {
            using DbConnection connection = database.OpenConnection();
            return !Upgrader(connection).IsUpgradeRequired();
        }

        /// <param name="only">Which of the scripts to run, every one by default; for tests to make a database as an earlier version left it.</param>
        internal static UpgradeEngine Upgrader(DbConnection connection, Func<string, bool>? only = null)
        {
            return DeployChanges.To
                .SqliteDatabase(new SharedConnection(connection))
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    (string scriptName) =>
                    {
                        return scriptName.Contains("Migrations") && scriptName.Split('.').Last() == "sql" && (only == null || only(scriptName));
                    })
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build();
        }
    }
}
