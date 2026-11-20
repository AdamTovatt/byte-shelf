using ByteShelf.Server.Access.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Repositories
{
    /// <summary>
    /// The <c>shelf_key_grant</c> table. Every write logs the change in the caller's transaction. A key
    /// goes with its last grant: <see cref="Services.ShelfKeyRemoval"/> holds that rule, so removals go
    /// through it.
    /// </summary>
    public interface IShelfKeyGrantRepository
    {
        /// <summary>The key's grant on the shelf, or null when it has none.</summary>
        Task<ShelfKeyGrant?> GetAsync(Guid shelfId, Guid keyId, IDbSession? session = null);

        /// <summary>Every grant the key holds with its shelf's name, oldest first.</summary>
        Task<IReadOnlyList<HeldShelfKeyGrant>> GetForKeyAsync(Guid keyId, IDbSession? session = null);

        /// <summary>Every grant held by a key that has one on the shelf, with its shelf's name, oldest first: the shelf's keys with all their shelves.</summary>
        Task<IReadOnlyList<HeldShelfKeyGrant>> GetForKeysOnShelfAsync(Guid shelfId, IDbSession? session = null);

        Task InsertAsync(ShelfKeyGrant grant, IDbSession session);

        /// <summary>Replaces a grant's permission and update time; logs the row as it is afterwards.</summary>
        Task UpdateAsync(ShelfKeyGrant grant, IDbSession session);

        /// <summary>Removes the key's grant on the shelf. False when there was none.</summary>
        Task<bool> DeleteAsync(Guid shelfId, Guid keyId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes every grant on the shelf; answers the keys that held one.</summary>
        Task<IReadOnlyList<Guid>> DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes every grant the key holds.</summary>
        Task DeleteForKeyAsync(Guid keyId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>
        /// Writes a grant as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different grant for the same key and shelf is removed first: the log brings it back if it
        /// still exists.
        /// </summary>
        Task UpsertReplicatedAsync(ShelfKeyGrant grant, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a grant by id; false when there is none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session);
    }
}
