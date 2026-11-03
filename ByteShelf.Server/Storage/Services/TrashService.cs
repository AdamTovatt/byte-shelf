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

        /// <summary>The deletion time emptying the trash gives what it holds: past any restore window.</summary>
        private static readonly DateTimeOffset ExpiredDeletionTime = DateTimeOffset.UnixEpoch;

        private readonly IDbSessionFactory _sessionFactory;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ITrashRepository _trashRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ShelfEncryption _encryption;
        private readonly ObjectCollector _objectCollector;
        private readonly ObjectStore _objectStore;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;

        public TrashService(
            IDbSessionFactory sessionFactory,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            ITrashRepository trashRepository,
            ShelfTree shelfTree,
            ShelfEncryption encryption,
            ObjectCollector objectCollector,
            ObjectStore objectStore,
            StorageOptions options,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _trashRepository = trashRepository;
            _shelfTree = shelfTree;
            _encryption = encryption;
            _objectCollector = objectCollector;
            _objectStore = objectStore;
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
        public async Task<DeletionTree> ListDeletionAsync(Shelf shelf, Guid deletionId, TreeQuery page)
        {
            TreeQuery normalized = page.Normalize();
            int limit = normalized.Limit!.Value;

            // A deletion never takes the root, so everything it took is under it.
            string? under = normalized.Under == ShelfPath.Root ? null : normalized.Under;

            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                TrashedEntry entry = await GetRestorableEntryAsync(shelf, deletionId, session);
                DirectoryRecord container = await _directoryRepository.GetByIdAsync(entry.ContainerId, session)
                    ?? throw new NotFoundException($"There is no restorable deletion {deletionId} in this shelf's trash.");
                string containerPath = await _shelfTree.GetDirectoryPathAsync(container.Id, session);

                // As a live tree's page: each kind one past the page, so the first limit + 1 of both are among them.
                IReadOnlyList<(DirectoryRecord Directory, string Path)> directories = await _directoryRepository.GetDeletionSubtreeAsync(
                    container.Id, containerPath, deletionId, under, normalized.After, limit + 1, session);
                IReadOnlyList<(FileRecord File, string Path)> files = await _fileRepository.GetDeletionSubtreeAsync(
                    container.Id, containerPath, deletionId, under, normalized.After, limit + 1, session);

                // A directory's own encryption counts, as it was the folder its files were stored in.
                Guid storedIn = under == null
                    ? entry.Kind == TrashEntryKind.Directory ? entry.EntryId : entry.ContainerId
                    : await DirectoryAtAsync(entry, container.Id, containerPath, under, session);

                return new DeletionTree
                {
                    Deletion = StorageContracts.ToTrashEntry(entry, containerPath, _options),
                    EncryptedBy = await _encryption.DescribeFolderOfAsync(storedIn, session),
                    Tree = DirectoryTree.Page(
                        StorageContracts.ToShelfDirectory(container, containerPath),
                        directories.Select(row => StorageContracts.ToShelfDirectory(row.Directory, row.Path)),
                        files.Select(row => StorageContracts.ToShelfFile(row.File, ShelfPath.GetParent(row.Path))),
                        limit),
                };
            }
        }

        /// <inheritdoc />
        public async Task<FileContent> GetDeletedContentAsync(Shelf shelf, Guid deletionId, Guid fileId)
        {
            ShelfFile described;
            string sha256;
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                await GetRestorableEntryAsync(shelf, deletionId, session);
                FileRecord? file = await _fileRepository.GetByIdAsync(fileId, session);
                if (file == null || file.ShelfId != shelf.Id || file.DeletionId != deletionId)
                {
                    throw new NotFoundException($"Deletion {deletionId} in this shelf's trash holds no file {fileId}.");
                }

                described = await _shelfTree.ToShelfFileAsync(file, session);
                sha256 = file.Sha256;
            }

            // A file in the trash keeps its contents until it is purged, which may come between the lookup
            // and the open: it is then gone, as though looked up a moment later.
            try
            {
                return new FileContent { File = described, Contents = _objectStore.OpenRead(sha256) };
            }
            catch (FileNotFoundException)
            {
                throw new NotFoundException($"File {fileId} has been purged from this shelf's trash.");
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
                await _encryption.RequireRestoreAsync(container.Id, deletionId, path, session);

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
            cancellationToken.ThrowIfCancellationRequested();

            // All of the trash goes past its window at once, so none of it can be restored from here on,
            // and a purge stopped part way (a crash, a cancelled request) leaves only expired rows, which
            // maintenance's next purge takes.
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                await _fileRepository.ExpireTrashAsync(shelf.Id, ExpiredDeletionTime, session);
                await _directoryRepository.ExpireTrashAsync(shelf.Id, ExpiredDeletionTime, session);
                await session.CommitAsync();
            }

            return await PurgeDeletedBeforeAsync(RestorableSince(), shelf.Id, cancellationToken);
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

        /// <summary>The directory deletion <paramref name="entry"/> took at <paramref name="path"/>, or the one the file it took there was in.</summary>
        /// <exception cref="NotFoundException">It took nothing at <paramref name="path"/>.</exception>
        private async Task<Guid> DirectoryAtAsync(TrashedEntry entry, Guid containerId, string containerPath, string path, IDbSession session)
        {
            // Each listing starts at the path, so what is there comes first.
            IReadOnlyList<(DirectoryRecord Directory, string Path)> directories = await _directoryRepository.GetDeletionSubtreeAsync(
                containerId, containerPath, entry.DeletionId, path, after: null, maxRows: 1, session);
            if (directories.Count == 1 && directories[0].Path == path)
            {
                return directories[0].Directory.Id;
            }

            IReadOnlyList<(FileRecord File, string Path)> files = await _fileRepository.GetDeletionSubtreeAsync(
                containerId, containerPath, entry.DeletionId, path, after: null, maxRows: 1, session);
            if (files.Count == 1 && files[0].Path == path)
            {
                return files[0].File.DirectoryId;
            }

            throw new NotFoundException($"Deletion {entry.DeletionId} took nothing at '{path}'.");
        }

        private async Task<TrashEntry> ToTrashEntryAsync(TrashedEntry entry, IDbSession session)
        {
            string containerPath = await _shelfTree.GetDirectoryPathAsync(entry.ContainerId, session);
            return StorageContracts.ToTrashEntry(entry, containerPath, _options);
        }
    }
}
