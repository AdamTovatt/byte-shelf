using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    public interface IShelfRepository
    {
        Task<Shelf?> GetByIdAsync(Guid id, IDbSession? session = null);

        Task<Shelf?> GetByNameAsync(string name, IDbSession? session = null);

        Task<IReadOnlyList<Shelf>> GetAllAsync(IDbSession? session = null);

        /// <summary>Inserts a shelf row and logs it. False when the name is taken.</summary>
        Task<bool> TryInsertAsync(Shelf shelf, IDbSession session);

        /// <summary>What counts against the shelf's quota right now.</summary>
        Task<ShelfUsage> GetUsageAsync(Guid shelfId, IDbSession? session = null);
    }
}
