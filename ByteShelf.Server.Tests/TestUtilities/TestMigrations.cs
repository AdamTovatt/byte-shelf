using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using EasyReasy.Database;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Makes a database as a server before a migration left it.</summary>
    internal static class TestMigrations
    {
        /// <summary>Runs every migration before <paramref name="first"/>, as "v0014", on the database.</summary>
        public static void RunBefore(string first, DatabaseFile database)
        {
            using DbConnection connection = database.OpenConnection();
            DbUp.Engine.DatabaseUpgradeResult result = DatabaseMigrator
                .Upgrader(connection, name => string.CompareOrdinal(name.Split('.')[^2], first) < 0)
                .PerformUpgrade();

            Assert.True(result.Successful, result.Error?.ToString());
            Assert.Contains(result.Scripts, script => script.Name.Contains($"v{int.Parse(first[1..], System.Globalization.CultureInfo.InvariantCulture) - 1:D4}_", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Scripts, script => script.Name.Contains(first, StringComparison.Ordinal));
        }

        /// <summary>The change log's entries in <paramref name="database"/>, oldest first.</summary>
        public static Task<IReadOnlyList<ChangeLogEntry>> ReadLogAsync(DatabaseFile database)
        {
            return new ChangeLogRepository(database, new DbSessionFactory(database)).GetAfterAsync(0, 1000);
        }
    }
}
