using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>
    /// Keys programs use shelves with: created by admins on a shelf, given other shelves by grants, stored
    /// hashed, checked on every request. A key always holds at least one grant: it is deleted with its last.
    /// </summary>
    public interface IShelfKeyService
    {
        /// <summary>The keys with a grant on the shelf, oldest first, each with all its shelves, without their secrets.</summary>
        Task<IReadOnlyList<ShelfKeyInfo>> GetForShelfAsync(Shelf shelf);

        /// <summary>Creates a key working on the shelf and answers its secret, which is never shown again.</summary>
        /// <exception cref="ArgumentException">The name or permission is not allowed.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has been deleted meanwhile.</exception>
        Task<CreatedShelfKey> CreateAsync(Shelf shelf, string name, ShelfKeyPermission permission);

        /// <summary>The shelves the key works on, oldest grant first.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such key.</exception>
        Task<IReadOnlyList<ShelfKeyGrantInfo>> GetGrantsAsync(Guid keyId);

        /// <summary>Gives the key the shelf, or changes what it may do there.</summary>
        /// <exception cref="ArgumentException">The permission is not one of <see cref="ShelfKeyPermission"/>.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such key, or the shelf has been deleted meanwhile.</exception>
        Task<ShelfKeyGrantInfo> SetGrantAsync(Shelf shelf, Guid keyId, ShelfKeyPermission permission);

        /// <summary>Takes the shelf from the key; the key is deleted when that was its last grant.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The key has no grant on the shelf.</exception>
        Task RemoveGrantAsync(Shelf shelf, Guid keyId);

        /// <summary>Deletes the key from every shelf; requests with it fail from then on.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such key.</exception>
        Task DeleteAsync(Guid keyId);

        /// <summary>The key a secret belongs to, or null when it belongs to none.</summary>
        Task<ShelfKey?> FindBySecretAsync(string secret);
    }
}
