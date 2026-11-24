using ByteShelf.Server.Metadata.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Keeps everything the server holds about files in step with the files: what was read from contents,
    /// whichever reader read it, never outlives their plaintext, and what plugins keep about a file stays with
    /// it. The one place that knows every table of metadata. Each change is made in the caller's transaction
    /// and logged, and the log forgets readings that go
    /// (<see cref="ChangeLog.Models.ChangeEntityType.ForgetsRemovedStates"/>).
    /// </summary>
    public sealed class MetadataUpkeep
    {
        private readonly IObjectMetadataRepository _objectMetadata;
        private readonly IContentPluginMetadataRepository _contentPluginMetadata;
        private readonly IFilePluginMetadataRepository _filePluginMetadata;

        public MetadataUpkeep(
            IObjectMetadataRepository objectMetadata,
            IContentPluginMetadataRepository contentPluginMetadata,
            IFilePluginMetadataRepository filePluginMetadata)
        {
            _objectMetadata = objectMetadata;
            _contentPluginMetadata = contentPluginMetadata;
            _filePluginMetadata = filePluginMetadata;
        }

        /// <summary>The file <paramref name="fileId"/>, just added, was stored over <paramref name="replacedFileId"/>: a new version of a file is still that file, so it keeps what plugins keep about it.</summary>
        public async Task StoredOverAsync(Guid replacedFileId, Guid fileId, DateTimeOffset at, IDbSession session)
        {
            await _filePluginMetadata.CopyAsync(replacedFileId, fileId, at, session);
        }

        /// <summary>
        /// The contents <paramref name="sha256"/> of the file <paramref name="fileId"/> are about to be swapped,
        /// in this transaction: what was read from them goes unless another plain file, live or in the trash,
        /// holds them. Called before the file changes, so the log has the removals before the swap: a replica
        /// applies it a page at a time, and none it has applied may leave there what was read from plaintext
        /// the file no longer holds, whether or not it ever had those contents.
        /// </summary>
        public async Task SwappingAsync(Guid fileId, string sha256, DateTimeOffset at, IDbSession session)
        {
            await _objectMetadata.DeleteUnheldByOthersAsync(sha256, fileId, at, session);
            await _contentPluginMetadata.DeleteUnheldByOthersAsync(sha256, fileId, at, session);
        }

        /// <summary>
        /// The contents of the file <paramref name="fileId"/> were swapped, in this transaction. What plugins
        /// keep about it stays, logged again after it: a replica that had not fetched the old contents passed
        /// over the file until now, and its documents with it.
        /// </summary>
        public async Task SwappedAsync(Guid fileId, DateTimeOffset at, IDbSession session)
        {
            await _filePluginMetadata.LogAgainAsync(fileId, at, session);
        }

        /// <summary>Removes what was read from any contents no plain file row, live or in the trash, holds; answers how many rows went.</summary>
        public async Task<int> RemoveUnheldAsync(DateTimeOffset at, IDbSession session)
        {
            return await _objectMetadata.DeleteUnheldAsync(at, session)
                + await _contentPluginMetadata.DeleteUnheldAsync(at, session);
        }
    }
}
