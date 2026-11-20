using ByteShelf.Server.Access.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Repositories
{
    /// <summary>
    /// The <c>shelf_key</c> table. Every write logs the change in the caller's transaction. A key's grants
    /// go before it, and a key goes with its last grant: <see cref="Services.ShelfKeyRemoval"/> holds those
    /// rules, so removals and replicated writes go through it.
    /// </summary>
    public interface IShelfKeyRepository
    {
        /// <summary>The key whose secret hashes to <paramref name="secretSha256"/>, or null.</summary>
        Task<ShelfKey?> GetBySecretSha256Async(string secretSha256, IDbSession? session = null);

        /// <summary>The key with <paramref name="id"/>, or null once it is deleted.</summary>
        Task<ShelfKey?> GetByIdAsync(Guid id, IDbSession? session = null);

        /// <summary>The keys with a grant on the shelf, oldest first.</summary>
        Task<IReadOnlyList<ShelfKey>> GetForShelfAsync(Guid shelfId, IDbSession? session = null);

        Task InsertAsync(ShelfKey key, IDbSession session);

        /// <summary>Deletes the key when it holds no grant any more. True when it was deleted.</summary>
        Task<bool> DeleteIfUngrantedAsync(Guid id, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Writes a key by id as a primary's change log holds it, logged as <paramref name="action"/>.</summary>
        Task UpsertReplicatedAsync(ShelfKey key, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a key by id; false when there is none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session);
    }
}
