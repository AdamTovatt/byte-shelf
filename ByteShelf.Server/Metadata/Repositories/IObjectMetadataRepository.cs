using ByteShelf.Server.Metadata.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>object_metadata</c> table. Every write logs the change in the caller's transaction, and a
    /// removal blanks the row's earlier states in the log (<see cref="ChangeLog.Models.ChangeEntityType.ForgetsRemovedStates"/>).
    /// </summary>
    public interface IObjectMetadataRepository
    {
        /// <summary>What was read from the contents <paramref name="sha256"/>; null when they have not been.</summary>
        Task<ObjectMetadata?> GetBySha256Async(string sha256, IDbSession? session = null);

        /// <summary>
        /// The SHA-256 of contents still to read: held by a live file that is not encrypted and whose
        /// type is in <see cref="ReadMediaTypes"/>, with no row yet. Those after
        /// <paramref name="after"/>, in order, each once, at most <paramref name="limit"/> of them.
        /// </summary>
        Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit, IDbSession? session = null);

        /// <summary>Adds what was read, logged as added; false, changing nothing, when those contents have a row already.</summary>
        Task<bool> InsertAsync(ObjectMetadata metadata, IDbSession session);

        /// <summary>Removes every row whose contents no file row, live or trashed, holds, each logged as purged; answers how many.</summary>
        Task<int> DeleteUnreferencedAsync(DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Removes the row of the contents <paramref name="sha256"/> when no file row holds them any more,
        /// logged as purged; false when it stays or there was none. A file whose contents are swapped for
        /// encrypted ones calls it in the same transaction, so nothing read from them outlives them.
        /// </summary>
        Task<bool> DeleteUnreferencedAsync(string sha256, DateTimeOffset at, IDbSession session);

        /// <summary>Writes a row as a primary's change log holds it, logged as <paramref name="action"/>.</summary>
        Task UpsertReplicatedAsync(ObjectMetadata metadata, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as purged; false when there was none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
