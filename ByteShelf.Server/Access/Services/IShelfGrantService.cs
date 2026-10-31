using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Which users may use a shelf, and how. Admins reach every shelf without a grant.</summary>
    public interface IShelfGrantService
    {
        /// <summary>Every user given access to the shelf, oldest grant first.</summary>
        Task<IReadOnlyList<ShelfAccessGrant>> GetForShelfAsync(Shelf shelf);

        /// <summary>Gives the user access to the shelf, or replaces the access they have.</summary>
        /// <exception cref="ArgumentException">The permission is not one of <see cref="ShelfAccessPermission"/>.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such user, or the shelf has been deleted meanwhile.</exception>
        Task<ShelfAccessGrant> SetAsync(Shelf shelf, Guid userId, ShelfAccessPermission permission);

        /// <summary>Takes the user's access to the shelf away.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The user has no access to the shelf.</exception>
        Task RemoveAsync(Shelf shelf, Guid userId);
    }
}
