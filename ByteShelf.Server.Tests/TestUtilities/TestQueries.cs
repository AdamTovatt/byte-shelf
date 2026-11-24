using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Looks at a server's tables directly, for what must not be in them.</summary>
    internal static class TestQueries
    {
        /// <param name="session">A session on the server's database.</param>
        /// <param name="from">The table to count the rows of, with the condition they meet.</param>
        public static Task<long> CountAsync(IDbSession session, string from)
        {
            return session.Connection.QuerySingleAsync<long>($"SELECT COUNT(*) FROM {from}", transaction: session.Transaction);
        }
    }
}
