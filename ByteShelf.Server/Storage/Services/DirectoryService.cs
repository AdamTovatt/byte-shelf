using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class DirectoryService : IDirectoryService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ITrashService _trashService;
        private readonly TimeProvider _timeProvider;

        public DirectoryService(
            IDbSessionFactory sessionFactory,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            ShelfTree shelfTree,
            ITrashService trashService,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
            _trashService = trashService;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<DirectoryListing> ListAsync(Shelf shelf, EntryReference reference)
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
        public async Task<ShelfDirectory> CreateAsync(Shelf shelf, string path)
        {
            string normalizedPath = ShelfPath.Normalize(path);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                DirectoryRecord directory = await _shelfTree.EnsureDirectoryAsync(shelf.Id, normalizedPath, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
                return StorageContracts.ToShelfDirectory(directory, normalizedPath);
            }
        }

        /// <inheritdoc />
        public async Task<ShelfDirectory> MoveAsync(Shelf shelf, EntryReference reference, string toPath)
        {
            string normalizedPath = ShelfPath.Normalize(toPath);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                DirectoryRecord directory = await _shelfTree.GetDirectoryAsync(shelf.Id, reference, session);
                if (directory.IsRoot)
                {
                    throw new ArgumentException("The root directory cannot be moved.");
                }

                string currentPath = await _shelfTree.GetDirectoryPathAsync(directory.Id, session);
                if (currentPath == normalizedPath)
                {
                    return StorageContracts.ToShelfDirectory(directory, currentPath);
                }

                // Checked on the paths, before the destination's parents are created: they would be
                // created inside the directory being moved.
                if (normalizedPath.StartsWith(currentPath + ShelfPath.Separator, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"'{currentPath}' cannot be moved into itself.");
                }

                await EnsurePathsFitAsync(directory.Id, normalizedPath, session);

                DateTimeOffset now = _timeProvider.GetUtcNow();
                (DirectoryRecord parent, string name) = await _shelfTree.PrepareDestinationAsync(shelf.Id, normalizedPath, now, session);
                DirectoryRecord moved = await _directoryRepository.MoveAsync(directory.Id, parent.Id, name, now, session)
                    ?? throw new NotFoundException($"There is no directory '{reference}' on this shelf.");
                await session.CommitAsync();

                return StorageContracts.ToShelfDirectory(moved, normalizedPath);
            }
        }

        /// <inheritdoc />
        public async Task<TrashEntry> DeleteAsync(Shelf shelf, EntryReference reference)
        {
            Guid deletionId;

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                DirectoryRecord directory = await _shelfTree.GetDirectoryAsync(shelf.Id, reference, session);
                if (directory.IsRoot)
                {
                    throw new ArgumentException("The root directory cannot be deleted; delete what is in it.");
                }

                deletionId = await _trashService.TrashDirectoryAsync(directory.Id, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            return await _trashService.GetEntryAsync(shelf, deletionId);
        }

        /// <summary>
        /// Refuses a move that would leave a path under the directory, live or in the trash, longer than
        /// <see cref="ShelfPath.MaxPathLength"/>: such a path could no longer be named.
        /// </summary>
        private async Task EnsurePathsFitAsync(Guid directoryId, string normalizedPath, IDbSession session)
        {
            int room = ShelfPath.MaxPathLength - normalizedPath.Length;
            IReadOnlyList<string> candidates = await _directoryRepository.GetRelativePathsLongerThanAsync(directoryId, room, session);
            if (candidates.Any(relativePath => relativePath.Length > room))
            {
                throw new ArgumentException(
                    $"Moving to '{normalizedPath}' would make a path under it longer than {ShelfPath.MaxPathLength} characters.");
            }
        }
    }
}
