using System.Data.Common;
using System.Reflection;
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

            UpgradeEngine upgrader = DeployChanges.To
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

            DatabaseUpgradeResult result = upgrader.PerformUpgrade();

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
    }
}
