using ByteShelf.Server.Metadata.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Keeps everything the server holds about files in step with the files: what was read from contents,
    /// whichever reader read it, never outlives their plaintext, and what plugins keep about a file stays with
    /// it. The one place that knows every table of metadata. Each change is made in the caller's transaction
    /// and logged, and the log forgets readings that go
    /// (<see cref="ChangeLog.Models.ChangeEntityType.ForgetsEarlierStates"/>).
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
        /// Runs <paramref name="swap"/>, which swaps the contents <paramref name="sha256"/> of the file
        /// <paramref name="fileId"/> for others and logs it, between what it does to metadata. Only
        /// <see cref="Storage.Repositories.FileRepository.SwapContentAsync"/> calls it, whose swap there is no
        /// other way to.
        /// </summary>
        /// <remarks>
        /// Before it, what was read from the contents goes unless another plain file, live or in the trash,
        /// holds them, so the log has the removals before the swap: a replica applies it a page at a time, and
        /// none it has applied may leave there what was read from plaintext the file no longer holds, whether
        /// or not it ever had those contents. After it, what plugins keep about the file is logged again: a
        /// replica that had not fetched the old contents passed over the file until now, and its documents
        /// with it.
        /// </remarks>
        public async Task<TSwapped> AroundSwapAsync<TSwapped>(Guid fileId, string sha256, DateTimeOffset at, IDbSession session, Func<Task<TSwapped>> swap)
        {
            await _objectMetadata.DeleteUnheldByOthersAsync(sha256, fileId, at, session);
            await _contentPluginMetadata.DeleteUnheldByOthersAsync(sha256, fileId, at, session);

            TSwapped swapped = await swap();

            await _filePluginMetadata.LogAgainAsync(fileId, at, session);
            return swapped;
        }

        /// <summary>
        /// Removes everything the plugin <paramref name="pluginId"/> keeps, installed or not: what its reader
        /// read, and its documents about files with their versions, which the log forgets too. Answers how many rows
        /// of each went.
        /// </summary>
        public async Task<(int Contents, int Files)> ForgetPluginAsync(string pluginId, DateTimeOffset at, IDbSession session)
        {
            return (await _contentPluginMetadata.DeleteOfPluginAsync(pluginId, at, session), await _filePluginMetadata.DeleteOfPluginAsync(pluginId, at, session));
        }

        /// <summary>Removes what was read from any contents no plain file row, live or in the trash, holds; answers how many rows went.</summary>
        public async Task<int> RemoveUnheldAsync(DateTimeOffset at, IDbSession session)
        {
            return await _objectMetadata.DeleteUnheldAsync(at, session)
                + await _contentPluginMetadata.DeleteUnheldAsync(at, session);
        }
    }
}
