using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Keys programs use a shelf with: created by admins, stored hashed, checked on every request.</summary>
    public interface IShelfKeyService
    {
        /// <summary>The shelf's keys, oldest first, without their secrets.</summary>
        Task<IReadOnlyList<ShelfKeyInfo>> GetForShelfAsync(Shelf shelf);

        /// <summary>Creates a key for the shelf and answers its secret, which is never shown again.</summary>
        /// <exception cref="ArgumentException">The name or permission is not allowed.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has been deleted meanwhile.</exception>
        Task<CreatedShelfKey> CreateAsync(Shelf shelf, string name, ShelfKeyPermission permission);

        /// <summary>Deletes one of the shelf's keys; requests with it fail from then on.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has no such key.</exception>
        Task DeleteAsync(Shelf shelf, Guid keyId);

        /// <summary>The key a secret belongs to, or null when it belongs to none.</summary>
        Task<ShelfKey?> FindBySecretAsync(string secret);
    }
}
