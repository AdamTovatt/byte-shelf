using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using EasyReasy.Database;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Reads a database a migration test made with <see cref="TestRoot.CreateBefore"/>.</summary>
    internal static class TestMigrations
    {
        /// <summary>The change log's entries in <paramref name="database"/>, oldest first.</summary>
        public static Task<IReadOnlyList<ChangeLogEntry>> ReadLogAsync(DatabaseFile database)
        {
            return new ChangeLogRepository(database, new DbSessionFactory(database)).GetAfterAsync(0, 1000);
        }
    }
}
