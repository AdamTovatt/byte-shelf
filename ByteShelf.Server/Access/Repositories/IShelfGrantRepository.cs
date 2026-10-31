using ByteShelf.Server.Access.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Repositories
{
    /// <summary>The <c>shelf_grant</c> table. Every write logs the change in the caller's transaction.</summary>
    public interface IShelfGrantRepository
    {
        /// <summary>The user's grant on the shelf, or null when they have none.</summary>
        Task<ShelfGrant?> GetAsync(Guid shelfId, Guid userId, IDbSession? session = null);

        /// <summary>Every grant on the shelf with the user holding it, oldest first.</summary>
        Task<IReadOnlyList<HeldShelfGrant>> GetForShelfAsync(Guid shelfId, IDbSession? session = null);

        /// <summary>Every grant the user holds.</summary>
        Task<IReadOnlyList<ShelfGrant>> GetForUserAsync(Guid userId, IDbSession? session = null);

        Task InsertAsync(ShelfGrant grant, IDbSession session);

        /// <summary>Replaces a grant's permission and update time; logs the row as it is afterwards.</summary>
        Task UpdateAsync(ShelfGrant grant, IDbSession session);

        /// <summary>Removes the user's access to the shelf. False when there was none.</summary>
        Task<bool> DeleteAsync(Guid shelfId, Guid userId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes every grant on the shelf, for deleting it.</summary>
        Task DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes every grant the user holds, for deleting them.</summary>
        Task DeleteForUserAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session);
    }
}
