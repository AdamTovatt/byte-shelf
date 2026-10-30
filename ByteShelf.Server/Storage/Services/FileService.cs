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
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ObjectStore _objectStore;
        private readonly ITrashService _trashService;
        private readonly TimeProvider _timeProvider;

        public FileService(
            IDbSessionFactory sessionFactory,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            ShelfTree shelfTree,
            ObjectStore objectStore,
            ITrashService trashService,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
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
        public async Task<DirectoryListing> ListDirectoryAsync(Shelf shelf, EntryReference reference)
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                DirectoryRecord directory = await _shelfTree.GetDirectoryAsync(shelf.Id, reference, session);
                string path = await _shelfTree.GetDirectoryPathAsync(directory.Id, session);

                IReadOnlyList<DirectoryRecord> directories = await _directoryRepository.GetLiveChildrenAsync(directory.Id, session);
                IReadOnlyList<FileRecord> files = await _fileRepository.GetLiveInDirectoryAsync(directory.Id, session);

                return new DirectoryListing
                {
                    Directory = StorageContracts.ToShelfDirectory(directory, path),
                    Directories = directories.Select(child => StorageContracts.ToShelfDirectory(child, ShelfPath.Combine(path, child.Name))).ToList(),
                    Files = files.Select(file => StorageContracts.ToShelfFile(file, path)).ToList(),
                };
            }
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
    }
}
