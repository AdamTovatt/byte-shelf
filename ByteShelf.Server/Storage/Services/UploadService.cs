using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class UploadService : IUploadService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IShelfRepository _shelfRepository;
        private readonly IUploadRepository _uploadRepository;
        private readonly IFileRepository _fileRepository;
        private readonly ShelfTree _shelfTree;
        private readonly ShelfEncryption _encryption;
        private readonly ITrashService _trashService;
        private readonly StagingArea _stagingArea;
        private readonly ObjectStore _objectStore;
        private readonly ObjectCollector _objectCollector;
        private readonly UploadLocks _uploadLocks;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;

        public UploadService(
            IDbSessionFactory sessionFactory,
            IShelfRepository shelfRepository,
            IUploadRepository uploadRepository,
            IFileRepository fileRepository,
            ShelfTree shelfTree,
            ShelfEncryption encryption,
            ITrashService trashService,
            StagingArea stagingArea,
            ObjectStore objectStore,
            ObjectCollector objectCollector,
            UploadLocks uploadLocks,
            StorageOptions options,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _shelfRepository = shelfRepository;
            _uploadRepository = uploadRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
            _encryption = encryption;
            _trashService = trashService;
            _stagingArea = stagingArea;
            _objectStore = objectStore;
            _objectCollector = objectCollector;
            _uploadLocks = uploadLocks;
            _options = options;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<ShelfFile> StoreAsync(Shelf shelf, StartUploadRequest request, string startedBy, Stream content, CancellationToken cancellationToken)
        {
            return await StoreInOneGoAsync(await BeginAsync(shelf, request, startedBy, isResumable: false), content, throughLink: false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ShelfFile> StoreThroughLinkAsync(Shelf shelf, StartUploadRequest request, string startedBy, Stream content, CancellationToken cancellationToken)
        {
            return await StoreInOneGoAsync(await BeginAsync(shelf, request, startedBy, isResumable: false), content, throughLink: true, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ShelfFile> SwapAsync(Shelf shelf, StartContentSwapRequest request, string startedBy, Stream content, CancellationToken cancellationToken)
        {
            return await StoreInOneGoAsync(await BeginSwapAsync(shelf, request, startedBy, isResumable: false), content, throughLink: false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<UploadSession> StartSwapAsync(Shelf shelf, StartContentSwapRequest request, string startedBy)
        {
            UploadRecord upload = await BeginSwapAsync(shelf, request, startedBy, isResumable: true);
            return StorageContracts.ToUploadSession(upload, _options);
        }

        /// <summary>Writes a single PUT's whole body to its upload and finishes it, discarding it on any failure.</summary>
        private async Task<ShelfFile> StoreInOneGoAsync(UploadRecord upload, Stream content, bool throughLink, CancellationToken cancellationToken)
        {
            using (await _uploadLocks.AcquireAsync(upload.Id, CancellationToken.None))
            {
                bool stored = false;
                try
                {
                    long written = await _stagingArea.WriteAsync(upload.Id, 0, content, upload.SizeBytes, cancellationToken);
                    if (written != upload.SizeBytes)
                    {
                        throw new ArgumentException($"The request declared {upload.SizeBytes} bytes but carried {written}.");
                    }

                    ShelfFile file = await FinishAsync(upload, throughLink, cancellationToken);
                    stored = true;
                    return file;
                }
                finally
                {
                    if (!stored)
                    {
                        await DiscardAsync(upload.Id);
                    }
                }
            }
        }

        /// <inheritdoc />
        public async Task<UploadSession> StartAsync(Shelf shelf, StartUploadRequest request, string startedBy)
        {
            UploadRecord upload = await BeginAsync(shelf, request, startedBy, isResumable: true);
            return StorageContracts.ToUploadSession(upload, _options);
        }

        /// <inheritdoc />
        public async Task<UploadRecord> GetAsync(Shelf shelf, Guid uploadId)
        {
            return await GetUploadAsync(shelf, uploadId);
        }

        /// <inheritdoc />
        public UploadSession ToSession(UploadRecord upload)
        {
            return StorageContracts.ToUploadSession(upload, _options);
        }

        /// <inheritdoc />
        public async Task<UploadSession> AppendAsync(Shelf shelf, Guid uploadId, long offset, Stream content, CancellationToken cancellationToken)
        {
            using (await _uploadLocks.AcquireAsync(uploadId, cancellationToken))
            {
                UploadRecord upload = await GetUploadAsync(shelf, uploadId);

                if (offset != upload.ReceivedBytes)
                {
                    throw new ConflictException($"The upload holds {upload.ReceivedBytes} bytes, so the next chunk must start at offset {upload.ReceivedBytes}, not {offset}.");
                }

                long written = await _stagingArea.WriteAsync(upload.Id, offset, content, upload.SizeBytes, cancellationToken);

                if (!await _uploadRepository.UpdateReceivedAsync(upload.Id, offset, offset + written, _timeProvider.GetUtcNow()))
                {
                    throw new NotFoundException($"There is no upload {uploadId} on this shelf.");
                }

                return StorageContracts.ToUploadSession(await GetUploadAsync(shelf, uploadId), _options);
            }
        }

        /// <inheritdoc />
        public async Task<ShelfFile> CompleteAsync(Shelf shelf, Guid uploadId, CancellationToken cancellationToken)
        {
            using (await _uploadLocks.AcquireAsync(uploadId, cancellationToken))
            {
                UploadRecord upload = await GetUploadAsync(shelf, uploadId);

                if (!upload.IsComplete)
                {
                    throw new ConflictException($"The upload holds {upload.ReceivedBytes} of its {upload.SizeBytes} bytes.");
                }

                return await FinishAsync(upload, throughLink: false, cancellationToken);
            }
        }

        /// <inheritdoc />
        public async Task AbortAsync(Shelf shelf, Guid uploadId)
        {
            using (await _uploadLocks.AcquireAsync(uploadId))
            {
                UploadRecord upload = await GetUploadAsync(shelf, uploadId);
                await DiscardAsync(upload.Id);
            }
        }

        /// <summary>
        /// Validates the request and records the upload, reserving its size, in one write transaction:
        /// SQLite runs one writer at a time, so two uploads can never both fit into the same free space.
        /// </summary>
        private async Task<UploadRecord> BeginAsync(Shelf shelf, StartUploadRequest request, string startedBy, bool isResumable)
        {
            string normalizedPath = ShelfPath.Normalize(request.Path);
            if (normalizedPath == ShelfPath.Root)
            {
                throw new ArgumentException("A file cannot be stored at the root itself; give it a name.");
            }

            bool overwrite = request.Overwrite;
            UploadRecord upload = NewUpload(
                shelf,
                startedBy,
                new StoreAtPath(normalizedPath, overwrite, FileDescription.Normalize(request.Description)),
                ContentTypes.ForUpload(request.ContentType, normalizedPath),
                request.SizeBytes,
                request.Encrypted,
                isResumable);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                // Refuse up front what finishing would refuse anyway, before any byte is sent. Finishing
                // checks again, since the shelf can change while the bytes arrive.
                await EnsureTargetAcceptsAsync(shelf.Id, normalizedPath, overwrite, upload.Encrypted, session);
                await ReserveAsync(shelf, upload, session);
                await session.CommitAsync();
            }

            _stagingArea.EnsureExists(upload.Id);
            return upload;
        }

        /// <summary>
        /// Validates a swap and records its upload, reserving what it adds, as <see cref="BeginAsync"/> does.
        /// The upload takes the file's type.
        /// </summary>
        private async Task<UploadRecord> BeginSwapAsync(Shelf shelf, StartContentSwapRequest request, string startedBy, bool isResumable)
        {
            // Stored and compared lowercase, as hashes are everywhere here; a client may send either case.
            string expectedSha256 = request.Sha256.ToLowerInvariant();
            if (!ObjectStore.IsValidHash(expectedSha256))
            {
                throw new ArgumentException($"'{request.Sha256}' is not a SHA-256: 64 hexadecimal digits.");
            }

            ContentSwap swap = new ContentSwap(request.FileId, expectedSha256);
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                (FileRecord file, _) = await GetSwappableAsync(shelf.Id, swap, request.Encrypted, session);
                UploadRecord upload = NewUpload(shelf, startedBy, swap, file.ContentType, request.SizeBytes, request.Encrypted, isResumable);
                await ReserveAsync(shelf, upload, session);
                await session.CommitAsync();
                _stagingArea.EnsureExists(upload.Id);
                return upload;
            }
        }

        /// <summary>A new upload to <paramref name="target"/>, nothing received yet.</summary>
        /// <exception cref="ArgumentException"><paramref name="sizeBytes"/> is negative.</exception>
        private UploadRecord NewUpload(Shelf shelf, string startedBy, UploadTarget target, string contentType, long sizeBytes, bool encrypted, bool isResumable)
        {
            if (sizeBytes < 0)
            {
                throw new ArgumentException("The size of an upload cannot be negative.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            return new UploadRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                StartedBy = startedBy,
                Target = target,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                ReceivedBytes = 0,
                Encrypted = encrypted,
                IsResumable = isResumable,
                CreatedAt = now,
                LastActivityAt = now,
            };
        }

        /// <summary>
        /// Records <paramref name="upload"/>, reserving against the shelf's quota what it will add, as the
        /// shelf's usage counts it: its size, or for a swap only what it adds to its file's.
        /// </summary>
        /// <exception cref="QuotaExceededException">The shelf has too little room.</exception>
        private async Task ReserveAsync(Shelf shelf, UploadRecord upload, IDbSession session)
        {
            Shelf current = await _shelfRepository.GetByIdAsync(shelf.Id, session)
                ?? throw new NotFoundException($"There is no shelf '{shelf.Name}'.");

            if (current.QuotaBytes is not long quota)
            {
                await _uploadRepository.InsertAsync(upload, session);
                return;
            }

            // Measured before and after, so the usage query alone says what an upload holds. Throwing
            // leaves the caller's transaction uncommitted, and the row with it.
            long before = (await _shelfRepository.GetUsageAsync(shelf.Id, session)).TotalBytes;
            await _uploadRepository.InsertAsync(upload, session);
            long after = (await _shelfRepository.GetUsageAsync(shelf.Id, session)).TotalBytes;
            if (after > quota)
            {
                throw new QuotaExceededException(
                    $"The shelf '{shelf.Name}' has {Math.Max(quota - before, 0)} bytes free of its {quota}, too few for {after - before} more.");
            }
        }

        /// <summary>
        /// Hashes the staged bytes, then in one transaction stores the file (<see cref="StoreNewAsync"/>)
        /// or swaps its contents (<see cref="SwapInAsync"/>) and removes the upload, placing the object last
        /// before the commit. The object's lock is held from placing it until the row commits, so no
        /// removal can take it away in between. A swap's old contents are removed once it commits, unless
        /// another file holds them. Hold the upload's lock. An upload <paramref name="throughLink"/> is held
        /// to <see cref="ShelfEncryption.RequireLinkTargetAsync"/> again in that transaction: the place may
        /// have been encrypted while the body arrived.
        /// </summary>
        private async Task<ShelfFile> FinishAsync(UploadRecord upload, bool throughLink, CancellationToken cancellationToken)
        {
            // The server cannot tell a right password, but it can tell what is no age file at all.
            if (upload.Encrypted && !await _stagingArea.StartsAsAgeFileAsync(upload.Id, cancellationToken))
            {
                throw new ArgumentException(upload.Target is StoreAtPath store
                    ? $"'{store.Path}' was sent as encrypted, but it is not an age file."
                    : "The new contents were sent as encrypted, but they are not an age file.");
            }

            string sha256 = await _stagingArea.ComputeSha256Async(upload.Id, cancellationToken);
            ShelfFile stored;

            using (await _objectStore.LockAsync(sha256, CancellationToken.None))
            {
                bool placed = false;
                try
                {
                    await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                    {
                        DateTimeOffset now = _timeProvider.GetUtcNow();
                        stored = upload.Target switch
                        {
                            StoreAtPath store => await StoreNewAsync(upload, store, sha256, throughLink, now, session),
                            ContentSwap swap => await SwapInAsync(upload, swap, sha256, now, session),
                            _ => throw new InvalidOperationException($"No upload stores to a {upload.Target.GetType().Name}."),
                        };
                        await _uploadRepository.DeleteAsync(upload.Id, session);

                        _objectStore.Place(_stagingArea.GetPath(upload.Id), sha256);
                        placed = true;
                        await session.CommitAsync();
                    }
                }
                catch when (placed)
                {
                    // The staged bytes are in the object store now, so the upload can never finish. The
                    // object stays unreferenced until the sweep removes it.
                    await DiscardAsync(upload.Id);
                    throw;
                }
            }

            if (upload.Target is ContentSwap committed)
            {
                await _objectCollector.RemoveReleasedAsync([committed.Sha256]);
            }

            return stored;
        }

        /// <summary>
        /// Creates missing directories, moves a file the upload replaces to the trash, and inserts the new
        /// file's row.
        /// </summary>
        private async Task<ShelfFile> StoreNewAsync(UploadRecord upload, StoreAtPath store, string sha256, bool throughLink, DateTimeOffset now, IDbSession session)
        {
            await EnsureTargetAcceptsAsync(upload.ShelfId, store.Path, store.Overwrite, upload.Encrypted, session);
            if (throughLink)
            {
                await _encryption.RequireLinkTargetAsync(upload.ShelfId, store.Path, session);
            }

            string directoryPath = ShelfPath.GetParent(store.Path);
            string name = ShelfPath.GetName(store.Path);
            DirectoryRecord directory = await _shelfTree.EnsureDirectoryAsync(upload.ShelfId, directoryPath, now, session);

            FileRecord? replaced = await _fileRepository.GetLiveByNameAsync(directory.Id, name, session);
            if (replaced != null)
            {
                await _trashService.TrashFileAsync(replaced.Id, now, session);
            }

            FileRecord file = new FileRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = upload.ShelfId,
                DirectoryId = directory.Id,
                Name = name,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
                Sha256 = sha256,
                CreatedAt = now,
                UpdatedAt = now,
                // A new version of a file is still that file: it keeps what it was
                // described as unless the upload says anew.
                Description = store.Description ?? replaced?.Description,
                Encrypted = upload.Encrypted,
            };

            await _fileRepository.InsertAsync(file, session);
            return StorageContracts.ToShelfFile(file, directoryPath);
        }

        /// <summary>
        /// Points the swap's file at the new contents in place, checked again as things stand now
        /// (<see cref="GetSwappableAsync"/>): the file may have moved, changed or gone since the swap started.
        /// </summary>
        private async Task<ShelfFile> SwapInAsync(UploadRecord upload, ContentSwap swap, string sha256, DateTimeOffset now, IDbSession session)
        {
            (FileRecord file, string path) = await GetSwappableAsync(upload.ShelfId, swap, upload.Encrypted, session);

            // The repository's own guard on the SHA-256 holds even for a caller that skipped the check above.
            FileRecord swapped = await _fileRepository.SwapContentAsync(file.Id, swap.Sha256, sha256, upload.SizeBytes, upload.Encrypted, now, session)
                ?? throw ChangedSinceRead(path);
            return StorageContracts.ToShelfFile(swapped, ShelfPath.GetParent(path));
        }

        /// <summary>
        /// The file <paramref name="swap"/> names and its path, as long as its contents may be swapped for
        /// ones <paramref name="encrypted"/> or not, as things stand: they are still the ones the client
        /// read, so a change made since is never lost, and <see cref="ShelfEncryption.RequireSwapAsync"/>
        /// allows it.
        /// </summary>
        /// <exception cref="NotFoundException">The file is gone, or in the trash.</exception>
        /// <exception cref="ConflictException">It may not be swapped.</exception>
        private async Task<(FileRecord File, string Path)> GetSwappableAsync(Guid shelfId, ContentSwap swap, bool encrypted, IDbSession session)
        {
            FileRecord file = await _shelfTree.GetFileAsync(shelfId, EntryReference.ForId(swap.FileId), session);
            string path = (await _shelfTree.ToShelfFileAsync(file, session)).Path;
            if (file.Sha256 != swap.Sha256)
            {
                throw ChangedSinceRead(path);
            }

            await _encryption.RequireSwapAsync(file, path, encrypted, session);
            return (file, path);
        }

        private static ConflictException ChangedSinceRead(string path)
        {
            return new ConflictException($"'{path}' has changed since its contents were read: read it again, then encrypt or decrypt it.");
        }

        /// <summary>Throws unless a file may be stored at <paramref name="normalizedPath"/> as things stand.</summary>
        private async Task EnsureTargetAcceptsAsync(Guid shelfId, string normalizedPath, bool overwrite, bool encrypted, IDbSession session)
        {
            await _encryption.RequireStoreAsync(shelfId, normalizedPath, encrypted, session);

            DirectoryRecord? directory = await _shelfTree.FindDirectoryAsync(shelfId, ShelfPath.GetParent(normalizedPath), session);
            if (directory == null)
            {
                // Missing directories are created when the upload finishes; a file in the way is
                // reported then.
                return;
            }

            await _shelfTree.EnsureNameAvailableAsync(directory.Id, ShelfPath.GetName(normalizedPath), normalizedPath, mayReplaceFile: overwrite, session);
        }

        private async Task<UploadRecord> GetUploadAsync(Shelf shelf, Guid uploadId)
        {
            UploadRecord? upload = await _uploadRepository.GetByIdAsync(uploadId);
            if (upload == null || upload.ShelfId != shelf.Id)
            {
                throw new NotFoundException($"There is no upload {uploadId} on this shelf.");
            }

            return upload;
        }

        /// <summary>Removes an upload's row and staged bytes, releasing its reservation. Hold the upload's lock.</summary>
        private async Task DiscardAsync(Guid uploadId)
        {
            await _uploadRepository.DeleteAsync(uploadId);
            _stagingArea.Delete(uploadId);
        }
    }
}
