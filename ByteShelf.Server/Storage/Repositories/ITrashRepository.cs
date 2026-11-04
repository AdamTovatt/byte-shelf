using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>Reads the trash as deletions rather than as rows.</summary>
    public interface ITrashRepository
    {
        /// <summary>The top entry of every deletion on the shelf made at or after <paramref name="deletedSince"/>, newest first.</summary>
        Task<IReadOnlyList<TrashedEntry>> GetEntriesAsync(Guid shelfId, DateTimeOffset deletedSince, IDbSession? session = null);

        /// <summary>The top entry of one deletion, or null when it is not on the shelf's trash.</summary>
        Task<TrashedEntry?> GetEntryAsync(Guid shelfId, Guid deletionId, IDbSession? session = null);
    }
}
