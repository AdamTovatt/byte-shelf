using ByteShelf.Server.Metadata.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>file_plugin_metadata</c> table. Every write logs the change in the caller's transaction; a row's
    /// going with its file is the database's doing and is not logged
    /// (<see cref="ChangeLog.Models.ChangeEntityType.OwnerOf"/>). Nothing read from a file's plaintext may be in
    /// one: it stays when the file is encrypted in place.
    /// </summary>
    public interface IFilePluginMetadataRepository
    {
        /// <summary>What a plugin keeps about a file, or the version of what it removed; null when it never kept anything.</summary>
        Task<FilePluginMetadata?> GetAsync(string pluginId, Guid fileId, IDbSession? session = null);

        /// <summary>
        /// Writes a plugin's document about a file, or removes it with a null <paramref name="document"/>, over
        /// the version <paramref name="expectedVersion"/>, null for a first write; answers the row as written,
        /// or null, changing nothing, when another version is there.
        /// </summary>
        /// <exception cref="System.Data.Common.DbException">There is no such file: the caller finds it in the same transaction.</exception>
        Task<FilePluginMetadata?> SetAsync(string pluginId, Guid fileId, int? expectedVersion, PluginDocument? document, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Gives the file <paramref name="toFileId"/>, which has none, every plugin's document about
        /// <paramref name="fromFileId"/> at the same version, a removed one's included.
        /// </summary>
        Task CopyAsync(Guid fromFileId, Guid toFileId, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Logs every document about a file again, as it stands, so that they follow its newest entry.
        /// </summary>
        Task LogAgainAsync(Guid fileId, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Writes a row as a primary's change log holds it, logged as <paramref name="action"/>. Its file may
        /// still be to come in the same transaction, whose foreign keys are then deferred.
        /// </summary>
        Task UpsertReplicatedAsync(FilePluginMetadata metadata, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes, logged as purged, every row whose file never came in a transaction that deferred the foreign keys.</summary>
        Task DeleteWithoutFileAsync(DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as purged; false when there was none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
