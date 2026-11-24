using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>Rows that go with another's unlogged (<see cref="ChangeEntityType.Owned"/>), as a replica applies them.</summary>
    internal static class OwnedRows
    {
        /// <summary>The ids of the rows whose <paramref name="owner"/> is not here, in a transaction that deferred the foreign keys.</summary>
        public static async Task<IReadOnlyList<Guid>> GetIdsWithoutOwnerAsync(EntityOwner owner, IDbSession session)
        {
            string query = $@"
                SELECT id FROM {owner.OwnedTable}
                WHERE NOT EXISTS (SELECT 1 FROM {owner.Table} WHERE {owner.Table}.id = {owner.OwnedTable}.{owner.OwnedColumn})";

            return (await session.Connection.QueryAsync<Guid>(query, transaction: session.Transaction)).ToList();
        }
    }
}
