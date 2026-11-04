using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>SQLite settings a session's transaction can take on.</summary>
    public static class DbSessionExtensions
    {
        /// <summary>
        /// Lets rows refer to ones written later in <paramref name="session"/>'s transaction, such as a row
        /// naming a parent a later write puts in place. The foreign keys are still checked, at commit.
        /// Lasts until the transaction ends; setting it twice is harmless.
        /// </summary>
        public static async Task DeferForeignKeysAsync(this IDbSession session)
        {
            await session.Connection.ExecuteAsync("PRAGMA defer_foreign_keys = ON", transaction: session.Transaction);
        }
    }
}
