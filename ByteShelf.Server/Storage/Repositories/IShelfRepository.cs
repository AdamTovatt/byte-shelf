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

        /// <summary>Replaces a shelf's name, quota and update time, and logs it. False when the name is taken.</summary>
        Task<bool> TryUpdateAsync(Shelf shelf, IDbSession session);

        /// <summary>Whether the shelf holds nothing: no file (trash included), no directory but its root, no upload.</summary>
        Task<bool> IsEmptyAsync(Guid shelfId, IDbSession? session = null);

        /// <summary>Removes an empty shelf's row and logs it. Remove its root directory first.</summary>
        Task DeleteAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>What counts against the shelf's quota right now.</summary>
        Task<ShelfUsage> GetUsageAsync(Guid shelfId, IDbSession? session = null);

        /// <summary>
        /// Whether every shelf with a quota that has a file row holding the contents, live or in the trash, would be within it
        /// with <paramref name="moreBytesPerRow"/> more for each such row, as a derived file of them counts. Asked in the
        /// write transaction that adds them, two adding at once cannot both take the last of the room.
        /// </summary>
        Task<bool> HasRoomForMoreOfAsync(string sha256, long moreBytesPerRow, IDbSession? session = null);

        /// <summary>How many bytes each shelf with a quota has left of it, at most 0 or less when it is over.</summary>
        Task<IReadOnlyDictionary<Guid, long>> GetFreeBytesAsync(IDbSession? session = null);

        /// <summary>
        /// Writes a shelf as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different shelf with the same name has it set aside first; the log gives it its own back.
        /// </summary>
        Task UpsertReplicatedAsync(Shelf shelf, string action, DateTimeOffset at, IDbSession session);
    }
}
