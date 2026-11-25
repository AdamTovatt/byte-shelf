using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>content_plugin_metadata</c> table. Every write logs the change in the caller's transaction, and
    /// a write or removal blanks the row's earlier states in the log (<see cref="ChangeLog.Models.ChangeEntityType.ForgetsEarlierStates"/>).
    /// </summary>
    public interface IContentPluginMetadataRepository
    {
        /// <summary>What a plugin's reader found in the contents <paramref name="sha256"/>; null when it has not read them.</summary>
        Task<ContentPluginMetadata?> GetAsync(string pluginId, string sha256, IDbSession? session = null);

        /// <summary>
        /// The SHA-256 of contents a plugin's reader has yet to read at <paramref name="readerVersion"/>: held
        /// by a live file that is not encrypted and whose type is in <paramref name="mediaTypes"/>. Those
        /// after <paramref name="after"/>, in order, each once, at most <paramref name="limit"/> of them.
        /// </summary>
        Task<IReadOnlyList<string>> GetUnreadAsync(string pluginId, int readerVersion, MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null);

        /// <summary>A plugin's rows by SHA-256 after <paramref name="afterSha256"/>, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<ContentPluginMetadata>> GetOfPluginAsync(string pluginId, string? afterSha256, int limit, IDbSession session);

        /// <summary>Removes a plugin's rows, each logged as purged; answers how many.</summary>
        Task<int> DeleteOfPluginAsync(string pluginId, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Writes what was read, over what another version of the reader read; false, changing nothing, when
        /// this version's is there already, or no live plain file holds those contents any more.
        /// </summary>
        Task<bool> RecordAsync(ContentPluginMetadata metadata, IDbSession session);

        /// <summary>Removes every row whose contents no plain file row, live or trashed, holds, each logged as purged; answers how many went.</summary>
        Task<int> DeleteUnheldAsync(DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Removes every plugin's row of the contents <paramref name="sha256"/>, logged as released (the file is swapped away from them), unless a plain file
        /// row, live or trashed, other than <paramref name="fileId"/> holds them.
        /// </summary>
        Task ReleaseUnheldByOthersAsync(string sha256, Guid fileId, DateTimeOffset at, IDbSession session);

        /// <summary>Writes a row as a primary's change log holds it, logged as <paramref name="action"/>.</summary>
        Task UpsertReplicatedAsync(ContentPluginMetadata metadata, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as purged; false when there was none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a row, logged as released, as a primary's release is applied; false when there was none.</summary>
        Task<bool> ReleaseByIdAsync(Guid id, DateTimeOffset at, IDbSession session);
    }
}
