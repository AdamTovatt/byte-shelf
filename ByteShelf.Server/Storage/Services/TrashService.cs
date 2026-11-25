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
        private readonly ContentsSessions _contentsSessions;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ITrashRepository _trashRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ShelfEncryption _encryption;
        private readonly ListedFiles _listedFiles;
        private readonly ObjectCollector _objectCollector;
        private readonly ObjectStore _objectStore;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TrashService> _logger;

        public TrashService(
            IDbSessionFactory sessionFactory,
            ContentsSessions contentsSessions,
            IDirectoryRepository directoryRepository,
            IFileRepository fileRepository,
            ITrashRepository trashRepository,
            ShelfTree shelfTree,
            ShelfEncryption encryption,
            ListedFiles listedFiles,
            ObjectCollector objectCollector,
            ObjectStore objectStore,
            StorageOptions options,
            TimeProvider timeProvider,
            ILogger<TrashService> logger)
        {
            _sessionFactory = sessionFactory;
            _contentsSessions = contentsSessions;
            _directoryRepository = directoryRepository;
            _fileRepository = fileRepository;
            _trashRepository = trashRepository;
            _shelfTree = shelfTree;
            _encryption = encryption;
            _listedFiles = listedFiles;
            _objectCollector = objectCollector;
            _objectStore = objectStore;
            _options = options;
            _timeProvider = timeProvider;
            _logger = logger;
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
                        await _listedFiles.OfAsync(files, session),
                        limit),
                };
            }
        }

        /// <inheritdoc />
        public async Task<FileRecord> GetDeletedFileAsync(Shelf shelf, Guid deletionId, Guid fileId)
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                return await GetDeletedFileAsync(shelf, deletionId, fileId, session);
            }
        }

        /// <inheritdoc />
        public async Task<FileContent> GetDeletedContentAsync(Shelf shelf, Guid deletionId, Guid fileId)
        {
            ShelfFile described;
            string sha256;
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                FileRecord file = await GetDeletedFileAsync(shelf, deletionId, fileId, session);
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
            Purge purge = await PurgeDeletedBeforeAsync(RestorableSince(), shelfId: null, cancellationToken);
            foreach (TracesNotRemovedException refused in purge.Refusals)
            {
                _logger.LogWarning(refused, "The files in the trash holding the contents {Sha256} stay there until the next purge.", refused.Sha256);
            }

            return purge.Files;
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

            Purge purge = await PurgeDeletedBeforeAsync(RestorableSince(), shelf.Id, cancellationToken);
            return purge.Refusals.Count == 0 ? purge.Files : throw purge.Refusals[0];
        }

        /// <summary>
        /// Removes every row deleted before <paramref name="cutoff"/>, on one shelf or all of them, in
        /// short transactions, files before the directories they were in. A batch of files commits once what
        /// the server has beside contents no plain file holds any more is gone (<see cref="ContentsSession"/>),
        /// and the objects only those files pointed at go after it.
        /// </summary>
        /// <remarks>
        /// A batch whose commit is refused, as what the server has of some contents could not be removed, is not
        /// purged, and the batches after it leave out the files holding those contents, which stay in the trash for
        /// the next purge: so the rest is purged, rather than that batch asked for again and again.
        /// </remarks>
        private async Task<Purge> PurgeDeletedBeforeAsync(DateTimeOffset cutoff, Guid? shelfId, CancellationToken cancellationToken)
        {
            int purgedFiles = 0;
            List<TracesNotRemovedException> refusals = new List<TracesNotRemovedException>();
            HashSet<string> skippedContents = new HashSet<string>(StringComparer.Ordinal);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<FileRecord> files = await _fileRepository.GetTrashedBeforeAsync(cutoff, shelfId, skippedContents, PurgeBatchSize);
                if (files.Count == 0)
                {
                    break;
                }

                IReadOnlyCollection<string> released;
                try
                {
                    await using ContentsSession session = await _contentsSessions.CreateAsync();
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    int purged = 0;
                    foreach (FileRecord file in files)
                    {
                        if (await _fileRepository.PurgeAsync(file.Id, now, session))
                        {
                            purged++;
                        }
                    }

                    await session.CommitAsync();
                    released = session.Released;
                    purgedFiles += purged;
                }
                catch (TracesNotRemovedException refused)
                {
                    refusals.Add(refused);
                    skippedContents.Add(refused.Sha256);
                    continue;
                }

                await _objectCollector.RemoveReleasedAsync(released, cancellationToken);
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

            return new Purge(purgedFiles, refusals);
        }

        /// <summary>
        /// The oldest deletion time still inside the restore window. Anything deleted earlier is due for
        /// the purge, which may already have taken part of it, so it is never shown or restored.
        /// </summary>
        private DateTimeOffset RestorableSince()
        {
            return _timeProvider.GetUtcNow() - _options.RestoreWindow;
        }

        private async Task<FileRecord> GetDeletedFileAsync(Shelf shelf, Guid deletionId, Guid fileId, IDbSession session)
        {
            await GetRestorableEntryAsync(shelf, deletionId, session);
            FileRecord? file = await _fileRepository.GetByIdAsync(fileId, session);
            if (file == null || file.ShelfId != shelf.Id || file.DeletionId != deletionId)
            {
                throw new NotFoundException($"Deletion {deletionId} in this shelf's trash holds no file {fileId}.");
            }

            return file;
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

        /// <summary>How many files a purge removed, and why it left any it was to remove.</summary>
        private sealed record Purge(int Files, IReadOnlyList<TracesNotRemovedException> Refusals);
    }
}
