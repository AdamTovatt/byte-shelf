using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// Whether a failed write was refused by a unique index or primary key, so a repository can answer
    /// "that name is already taken" as data rather than as a fault.
    /// </summary>
    public static class SqliteUniqueViolation
    {
        // The extended codes SQLITE_CONSTRAINT_PRIMARYKEY and SQLITE_CONSTRAINT_UNIQUE, never the shared
        // primary code 19, which NOT NULL, CHECK and foreign-key violations report as well.
        private const int SqliteConstraintPrimaryKey = 1555;
        private const int SqliteConstraintUnique = 2067;

        public static bool Is(DbException exception)
        {
            return exception is SqliteException sqlite
                && (sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique || sqlite.SqliteExtendedErrorCode == SqliteConstraintPrimaryKey);
        }
    }
}
