using ByteShelf.Server.Shared.Database;
using DbUp;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Makes a database as a server before a migration left it.</summary>
    internal static class TestMigrations
    {
        /// <summary>Runs every migration before <paramref name="first"/>, as "v0014", on the database.</summary>
        public static void RunBefore(string first, string connectionString)
        {
            DbUp.Engine.DatabaseUpgradeResult result = DeployChanges.To
                .SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(DatabaseMigrator).Assembly,
                    (string name) => name.Contains("Migrations") && name.EndsWith(".sql", StringComparison.Ordinal)
                        && string.CompareOrdinal(name.Split('.')[^2], first) < 0)
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build()
                .PerformUpgrade();

            Assert.True(result.Successful, result.Error?.ToString());
            Assert.Contains(result.Scripts, script => script.Name.Contains($"v{int.Parse(first[1..], System.Globalization.CultureInfo.InvariantCulture) - 1:D4}_", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Scripts, script => script.Name.Contains(first, StringComparison.Ordinal));
        }
    }
}
