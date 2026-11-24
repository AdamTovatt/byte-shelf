using ByteShelf.Server.Metadata.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>file_plugin_metadata</c> table. Every write logs the change in the caller's transaction; a row's
    /// going with its file is the database's doing and is not logged
    /// (<see cref="ChangeLog.Models.ChangeEntityType.Owned"/>). Nothing read from a file's plaintext may be in
    /// one: it stays when the file is encrypted in place.
    /// </summary>
    public interface IFilePluginMetadataRepository
    {
        /// <summary>What a plugin keeps about a file, or the version of what it removed; null when it never kept anything.</summary>
        Task<FilePluginMetadata?> GetAsync(string pluginId, Guid fileId, IDbSession? session = null);

        /// <summary>A plugin's rows by file id after <paramref name="afterFileId"/>, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<FilePluginMetadata>> GetOfPluginAsync(string pluginId, Guid? afterFileId, int limit, IDbSession session);

        /// <summary>Removes a plugin's rows with the versions they kept, each logged as purged; answers how many.</summary>
        Task<int> DeleteOfPluginAsync(string pluginId, DateTimeOffset at, IDbSession session);

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

        /// <summary>Logs every document about the files <paramref name="fileIds"/> again, as it stands, so that they follow the files' newest entries.</summary>
        Task LogAgainAsync(IReadOnlyCollection<Guid> fileIds, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Writes a row as a primary's change log holds it, logged as <paramref name="action"/>. Its file may
        /// still be to come in the same transaction, whose foreign keys are then deferred.
        /// </summary>
        Task UpsertReplicatedAsync(FilePluginMetadata metadata, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as purged; false when there was none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
