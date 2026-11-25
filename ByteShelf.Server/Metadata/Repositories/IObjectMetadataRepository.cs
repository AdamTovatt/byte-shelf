using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>object_metadata</c> table. Every write logs the change in the caller's transaction, and a
    /// later write or removal blanks the row's earlier states in the log (<see cref="ChangeLog.Models.ChangeEntityType.ForgetsEarlierStates"/>).
    /// </summary>
    public interface IObjectMetadataRepository
    {
        /// <summary>What was read from the contents <paramref name="sha256"/>; null when they have not been.</summary>
        Task<ObjectMetadata?> GetBySha256Async(string sha256, IDbSession? session = null);

        /// <summary>
        /// The SHA-256 of contents still to read: held by a live file that is not encrypted and whose
        /// type is in <paramref name="mediaTypes"/>, with no row that <paramref name="readerVersion"/> read. Those
        /// after <paramref name="after"/>, in order, each once, at most <paramref name="limit"/> of them.
        /// </summary>
        Task<IReadOnlyList<string>> GetUnreadAsync(int readerVersion, MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null);

        /// <summary>
        /// Adds what was read, logged as added, or writes it over what another version read, logged as
        /// updated; false, changing nothing, when those contents have a row of that version already, or no
        /// live plain file holds them any more.
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
