using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class FileService : IFileService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ShelfEncryption _encryption;
        private readonly ObjectStore _objectStore;
        private readonly ITrashService _trashService;
        private readonly TimeProvider _timeProvider;

        public FileService(
            IDbSessionFactory sessionFactory,
            IFileRepository fileRepository,
            ShelfTree shelfTree,
            ShelfEncryption encryption,
            ObjectStore objectStore,
            ITrashService trashService,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
            _encryption = encryption;
            _objectStore = objectStore;
            _trashService = trashService;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<ShelfFile> GetFileAsync(Shelf shelf, EntryReference reference)
        {
            FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference);
            return await _shelfTree.ToShelfFileAsync(file);
        }

        /// <inheritdoc />
        public async Task<FileContent> GetContentAsync(Shelf shelf, EntryReference reference)
        {
            FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference);

            return new FileContent
            {
                File = await _shelfTree.ToShelfFileAsync(file),
                ObjectPath = _objectStore.GetPath(file.Sha256),
            };
        }

        /// <inheritdoc />
        public async Task<TrashEntry> DeleteFileAsync(Shelf shelf, EntryReference reference)
        {
            Guid deletionId;

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference, session);
                deletionId = await _trashService.TrashFileAsync(file.Id, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            return await _trashService.GetEntryAsync(shelf, deletionId);
        }

        /// <inheritdoc />
        public async Task<ShelfFile> MoveFileAsync(Shelf shelf, EntryReference reference, string toPath)
        {
            string normalizedPath = ShelfPath.Normalize(toPath);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference, session);
                ShelfFile current = await _shelfTree.ToShelfFileAsync(file, session);
                if (current.Path == normalizedPath)
                {
                    return current;
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                (DirectoryRecord parent, string name) = await _shelfTree.PrepareDestinationAsync(shelf.Id, normalizedPath, now, session);
                await _encryption.RequireMoveAsync(movedDirectoryId: null, file.DirectoryId, parent.Id, current.Path, session);
                FileRecord moved = await _fileRepository.MoveAsync(file.Id, parent.Id, name, now, session)
                    ?? throw new NotFoundException($"There is no file '{reference}' on this shelf.");
                await session.CommitAsync();

                return StorageContracts.ToShelfFile(moved, ShelfPath.GetParent(normalizedPath));
            }
        }

        /// <inheritdoc />
        public async Task<ShelfFile> SetDescriptionAsync(Shelf shelf, EntryReference reference, string? description)
        {
            string? normalized = FileDescription.Normalize(description);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                FileRecord file = await _shelfTree.GetFileAsync(shelf.Id, reference, session);
                if (file.Description == normalized)
                {
                    return await _shelfTree.ToShelfFileAsync(file, session);
                }

                FileRecord updated = await _fileRepository.SetDescriptionAsync(file.Id, normalized, _timeProvider.GetUtcNow(), session)
                    ?? throw new NotFoundException($"There is no file '{reference}' on this shelf.");
                ShelfFile result = await _shelfTree.ToShelfFileAsync(updated, session);
                await session.CommitAsync();
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<FileSearchResults> SearchAsync(Shelf shelf, FileSearch search)
        {
            FileSearch normalized = search.Normalize();
            DirectoryRecord directory = await _shelfTree.GetDirectoryAsync(shelf.Id, EntryReference.ForPath(normalized.Under!));

            // One more than a page, to tell whether another page follows without a count.
            int limit = normalized.Limit!.Value;
            IReadOnlyList<(FileRecord File, string Path)> found = await _fileRepository.SearchAsync(directory.Id, normalized.Under!, normalized, limit + 1);
            List<ShelfFile> files = found.Take(limit).Select(row => StorageContracts.ToShelfFile(row.File, ShelfPath.GetParent(row.Path))).ToList();

            return new FileSearchResults
            {
                Files = files,
                NextCursor = found.Count > limit ? files[^1].Path : null,
            };
        }
    }
}
