using ByteShelf.Server.Access.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Repositories
{
    /// <summary>The <c>shelf_key</c> table. Every write logs the change in the caller's transaction.</summary>
    public interface IShelfKeyRepository
    {
        /// <summary>The key whose secret hashes to <paramref name="secretSha256"/>, or null.</summary>
        Task<ShelfKey?> GetBySecretSha256Async(string secretSha256, IDbSession? session = null);

        /// <summary>The key with <paramref name="id"/>, or null once it is deleted.</summary>
        Task<ShelfKey?> GetByIdAsync(Guid id, IDbSession? session = null);

        /// <summary>The shelf's keys, oldest first.</summary>
        Task<IReadOnlyList<ShelfKey>> GetForShelfAsync(Guid shelfId, IDbSession? session = null);

        Task InsertAsync(ShelfKey key, IDbSession session);

        /// <summary>Removes one of the shelf's keys. False when the shelf has no such key.</summary>
        Task<bool> DeleteAsync(Guid shelfId, Guid keyId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>
        /// Writes a key as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different key with the same secret is removed first: the log brings it back if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(ShelfKey key, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a key by id; false when there is none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes every key of the shelf, for deleting it.</summary>
        Task DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);
    }
}
