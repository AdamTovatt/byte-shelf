using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class TrashService : ITrashService
    {
        /// <summary>How many rows one purge transaction removes.</summary>
        internal const int PurgeBatchSize = 500;

        private readonly IDbSessionFactory _sessionFactory;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ITrashRepository _trashRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ObjectCollector _objectCollector;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;

        public TrashService(
            IDbSessionFactory sessionFactory,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            ITrashRepository trashRepository,
            ShelfTree shelfTree,
            ObjectCollector objectCollector,
            StorageOptions options,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _trashRepository = trashRepository;
            _shelfTree = shelfTree;
            _objectCollector = objectCollector;
            _options = options;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<Guid> TrashFileAsync(Guid fileId, DateTimeOffset at, IDbSession session)
        {
            Guid deletionId = Guid.NewGuid();

            if (!await _fileRepository.SoftDeleteAsync(fileId, deletionId, at, session))
            {
                throw new NotFoundException($"There is no file {fileId} to delete.");
            }

            return deletionId;
        }

        /// <inheritdoc />
        public async Task<Guid> TrashDirectoryAsync(Guid directoryId, DateTimeOffset at, IDbSession session)
        {
            Guid deletionId = Guid.NewGuid();

            IReadOnlyList<Guid> directoryIds = await _directoryRepository.SoftDeleteSubtreeAsync(directoryId, deletionId, at, session);
            if (directoryIds.Count == 0)
            {
                throw new NotFoundException($"There is no directory {directoryId} to delete.");
            }

            await _fileRepository.SoftDeleteInDirectoriesAsync(directoryIds, deletionId, at, session);
            return deletionId;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TrashEntry>> GetTrashAsync(Shelf shelf)
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                IReadOnlyList<TrashedEntry> entries = await _trashRepository.GetEntriesAsync(shelf.Id, RestorableSince(), session);
                List<TrashEntry> result = new List<TrashEntry>(entries.Count);

                foreach (TrashedEntry entry in entries)
                {
                    result.Add(await ToTrashEntryAsync(entry, session));
                }

                return result;
            }
        }

        /// <inheritdoc />
        public async Task<TrashEntry> GetEntryAsync(Shelf shelf, Guid deletionId)
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                TrashedEntry entry = await GetRestorableEntryAsync(shelf, deletionId, session);
                return await ToTrashEntryAsync(entry, session);
            }
        }

        /// <inheritdoc />
        public async Task RestoreAsync(Shelf shelf, Guid deletionId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                TrashedEntry entry = await GetRestorableEntryAsync(shelf, deletionId, session);

                DirectoryRecord? container = await _directoryRepository.GetByIdAsync(entry.ContainerId, session);
                string containerPath = await _shelfTree.GetDirectoryPathAsync(entry.ContainerId, session);
                string path = ShelfPath.Combine(containerPath, entry.Name);

                if (container == null || container.DeletedAt != null)
                {
                    throw new ConflictException($"'{containerPath}' is in the trash itself; restore it before '{path}'.");
                }

                await _shelfTree.EnsureNameAvailableAsync(container.Id, entry.Name, path, mayReplaceFile: false, session);

                DateTimeOffset now = _timeProvider.GetUtcNow();
                await _directoryRepository.RestoreDeletionAsync(deletionId, now, session);
                await _fileRepository.RestoreDeletionAsync(deletionId, now, session);

                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            return await PurgeDeletedBeforeAsync(RestorableSince(), shelfId: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> EmptyAsync(Shelf shelf, CancellationToken cancellationToken)
        {
            return await PurgeDeletedBeforeAsync(DateTimeOffset.MaxValue, shelf.Id, cancellationToken);
        }

        /// <summary>
        /// Removes every row deleted before <paramref name="cutoff"/>, on one shelf or all of them, in
        /// short transactions, files before the directories they were in. Then removes the objects only
        /// those files pointed at.
        /// </summary>
        private async Task<int> PurgeDeletedBeforeAsync(DateTimeOffset cutoff, Guid? shelfId, CancellationToken cancellationToken)
        {
            int purgedFiles = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<FileRecord> files = await _fileRepository.GetTrashedBeforeAsync(cutoff, shelfId, PurgeBatchSize);
                if (files.Count == 0)
                {
                    break;
                }

                await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    foreach (FileRecord file in files)
                    {
                        if (await _fileRepository.PurgeAsync(file.Id, now, session))
                        {
                            purgedFiles++;
                        }
                    }

                    await session.CommitAsync();
                }

                foreach (string sha256 in files.Select(file => file.Sha256).Distinct())
                {
                    await _objectCollector.RemoveIfUnreferencedAsync(sha256, cancellationToken);
                }
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<DirectoryRecord> directories = await _directoryRepository.GetPurgeableBeforeAsync(cutoff, shelfId, PurgeBatchSize);
                if (directories.Count == 0)
                {
                    break;
                }

                await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    foreach (DirectoryRecord directory in directories)
                    {
                        await _directoryRepository.PurgeAsync(directory.Id, now, session);
                    }

                    await session.CommitAsync();
                }
            }

            return purgedFiles;
        }

        /// <summary>
        /// The oldest deletion time still inside the restore window. Anything deleted earlier is due for
        /// the purge, which may already have taken part of it, so it is never shown or restored.
        /// </summary>
        private DateTimeOffset RestorableSince()
        {
            return _timeProvider.GetUtcNow() - _options.RestoreWindow;
        }

        private async Task<TrashedEntry> GetRestorableEntryAsync(Shelf shelf, Guid deletionId, IDbSession session)
        {
            TrashedEntry? entry = await _trashRepository.GetEntryAsync(shelf.Id, deletionId, session);
            if (entry == null || entry.DeletedAt < RestorableSince())
            {
                throw new NotFoundException($"There is no restorable deletion {deletionId} in this shelf's trash.");
            }

            return entry;
        }

        private async Task<TrashEntry> ToTrashEntryAsync(TrashedEntry entry, IDbSession session)
        {
            string containerPath = await _shelfTree.GetDirectoryPathAsync(entry.ContainerId, session);
            return StorageContracts.ToTrashEntry(entry, containerPath, _options);
        }
    }
}
