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
        /// type is in <paramref name="mediaTypes"/>, with no row yet. Those after
        /// <paramref name="after"/>, in order, each once, at most <paramref name="limit"/> of them.
        /// </summary>
        Task<IReadOnlyList<string>> GetUnreadAsync(MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null);

        /// <summary>
        /// Adds what was read, logged as added; false, changing nothing, when those contents have a row
        /// already, or no live plain file holds them any more.
        /// </summary>
        Task<bool> RecordAsync(ObjectMetadata metadata, IDbSession session);

        /// <summary>Removes every row whose contents no plain file row, live or trashed, holds, each logged as purged; answers how many went.</summary>
        Task<int> DeleteUnheldAsync(DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Removes the row of the contents <paramref name="sha256"/>, logged as purged, unless a plain file
        /// row, live or trashed, other than <paramref name="fileId"/> holds them.
        /// </summary>
        Task DeleteUnheldByOthersAsync(string sha256, Guid fileId, DateTimeOffset at, IDbSession session);

        /// <summary>Writes a row as a primary's change log holds it, logged as <paramref name="action"/>.</summary>
        Task UpsertReplicatedAsync(ObjectMetadata metadata, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as purged; false when there was none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
