using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Works out what a caller may do on a shelf, and which shelves it sees: the one place that rule lives.</summary>
    public interface IShelfAccessResolver
    {
        /// <summary>
        /// The caller's access to <paramref name="shelf"/>. A caller with no access at all is told the
        /// shelf does not exist, so shelf names do not leak.
        /// </summary>
        /// <exception cref="Shared.Errors.NotFoundException">The caller has no access to the shelf.</exception>
        Task<ShelfAccess> ResolveAsync(ShelfCaller caller, Shelf shelf);

        /// <summary>Every shelf the caller has any access to, by name.</summary>
        Task<IReadOnlyList<Shelf>> GetVisibleShelvesAsync(ShelfCaller caller);
    }
}
