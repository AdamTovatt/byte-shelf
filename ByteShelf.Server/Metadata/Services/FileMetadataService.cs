using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>What is known about a file besides its record: what was read from its contents, and what plugins keep.</summary>
    public class FileMetadataService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly ShelfTree _shelfTree;
        private readonly InstalledPlugins _plugins;
        private readonly IObjectMetadataRepository _objectMetadata;
        private readonly IContentPluginMetadataRepository _contentDocuments;
        private readonly IFilePluginMetadataRepository _fileDocuments;
        private readonly TimeProvider _timeProvider;

        public FileMetadataService(
            IDbSessionFactory sessionFactory,
            ShelfTree shelfTree,
            InstalledPlugins plugins,
            IObjectMetadataRepository objectMetadata,
            IContentPluginMetadataRepository contentDocuments,
            IFilePluginMetadataRepository fileDocuments,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _shelfTree = shelfTree;
            _plugins = plugins;
            _objectMetadata = objectMetadata;
            _contentDocuments = contentDocuments;
            _fileDocuments = fileDocuments;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// What the file's contents were read to say, nothing for an encrypted file or a type not read and
        /// readable with nothing yet for one not read yet, with what installed plugins keep about it.
        /// </summary>
        /// <exception cref="NotFoundException">There is no such live file.</exception>
        public async Task<FileMetadata> GetAsync(Shelf shelf, EntryReference reference)
        {
            await using IDbSession session = await _sessionFactory.CreateSessionAsync();
            FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference, session);

            // The row is the contents', which an encrypted file's are not to be read for, even when a
            // plain file holds the same bytes.
            bool readable = !file.Encrypted && ReadMediaTypes.All.Includes(ContentTypes.MediaTypeOf(file.ContentType));
            ObjectMetadata? read = readable ? await _objectMetadata.GetBySha256Async(file.Sha256, session) : null;

            Dictionary<string, PluginFileMetadata> plugins = new Dictionary<string, PluginFileMetadata>(StringComparer.Ordinal);
            foreach (PluginDeclaration plugin in _plugins.All)
            {
                PluginFileMetadata kept = await OfPluginAsync(plugin, file, session);
                if (kept.ContentsDocument != null || kept.FileVersion != null)
                {
                    plugins.Add(plugin.Id, kept);
                }
            }

            return StorageContracts.ToFileMetadata(readable, read, plugins);
        }

        /// <summary>
        /// Writes a plugin's document about a file, or removes it, over the version the request names; answers
        /// what the plugin keeps about the file then. Removing one that was never written changes nothing. The
        /// file's record stays as it is.
        /// </summary>
        /// <exception cref="NotFoundException">There is no such live file, or the plugin is not installed.</exception>
        /// <exception cref="ConflictException">Another version is there.</exception>
        public async Task<PluginFileMetadata> SetPluginDocumentAsync(Shelf shelf, EntryReference reference, string pluginId, SetPluginMetadataRequest request)
        {
            PluginDeclaration plugin = _plugins.Find(pluginId)
                ?? throw new NotFoundException($"There is no plugin '{pluginId}' on this server.");

            await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
            FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference, session);
            FilePluginMetadata? current = await _fileDocuments.GetAsync(plugin.Id, file.Id, session);
            bool nothingToRemove = current == null && request.Document == null && request.ExpectedVersion == null;
            if (!nothingToRemove
                && await _fileDocuments.SetAsync(plugin.Id, file.Id, request.ExpectedVersion, request.Document, _timeProvider.GetUtcNow(), session) == null)
            {
                throw new ConflictException(
                    $"What the plugin '{plugin.Id}' keeps about '{reference}' has changed since it was read ({At(current?.Version)}, not {At(request.ExpectedVersion)}): read it again, then write it.");
            }

            PluginFileMetadata kept = await OfPluginAsync(plugin, file, session);
            await session.CommitAsync();
            return kept;
        }

        private static string At(int? version)
        {
            return version == null ? "none written yet" : $"version {version}";
        }

        private async Task<PluginFileMetadata> OfPluginAsync(PluginDeclaration plugin, FileRecord file, IDbSession session)
        {
            ContentPluginMetadata? contents = file.Encrypted ? null : await _contentDocuments.GetAsync(plugin.Id, file.Sha256, session);
            FilePluginMetadata? about = await _fileDocuments.GetAsync(plugin.Id, file.Id, session);
            return PluginFileMetadata.Of(plugin, contents?.Document, about?.Document, about?.Version);
        }
    }
}
