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
        private readonly ITrashService _trashService;
        private readonly StagingArea _stagingArea;
        private readonly ObjectStore _objectStore;
        private readonly UploadLocks _uploadLocks;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;

        public UploadService(
            IDbSessionFactory sessionFactory,
            IShelfRepository shelfRepository,
            IUploadRepository uploadRepository,
            IFileRepository fileRepository,
            ShelfTree shelfTree,
            ITrashService trashService,
            StagingArea stagingArea,
            ObjectStore objectStore,
            UploadLocks uploadLocks,
            StorageOptions options,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _shelfRepository = shelfRepository;
            _uploadRepository = uploadRepository;
            _fileRepository = fileRepository;
            _shelfTree = shelfTree;
            _trashService = trashService;
            _stagingArea = stagingArea;
            _objectStore = objectStore;
            _uploadLocks = uploadLocks;
            _options = options;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<ShelfFile> StoreAsync(Shelf shelf, string path, string? contentType, long sizeBytes, bool overwrite, Stream content, CancellationToken cancellationToken)
        {
            UploadRecord upload = await BeginAsync(shelf, path, contentType, sizeBytes, overwrite, isResumable: false);

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

                    ShelfFile file = await FinishAsync(upload, cancellationToken);
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
        public async Task<UploadSession> StartAsync(Shelf shelf, StartUploadRequest request)
        {
            UploadRecord upload = await BeginAsync(shelf, request.Path, request.ContentType, request.SizeBytes, request.Overwrite, isResumable: true);
            return StorageContracts.ToUploadSession(upload, _options);
        }

        /// <inheritdoc />
        public async Task<UploadSession> GetAsync(Shelf shelf, Guid uploadId)
        {
            return StorageContracts.ToUploadSession(await GetUploadAsync(shelf, uploadId), _options);
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

                return await FinishAsync(upload, cancellationToken);
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
        private async Task<UploadRecord> BeginAsync(Shelf shelf, string path, string? contentType, long sizeBytes, bool overwrite, bool isResumable)
        {
            string normalizedPath = ShelfPath.Normalize(path);
            if (normalizedPath == ShelfPath.Root)
            {
                throw new ArgumentException("A file cannot be stored at the root itself; give it a name.");
            }

            if (sizeBytes < 0)
            {
                throw new ArgumentException("The size of an upload cannot be negative.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            UploadRecord upload = new UploadRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                Path = normalizedPath,
                ContentType = ContentTypes.Normalize(contentType),
                SizeBytes = sizeBytes,
                ReceivedBytes = 0,
                Overwrite = overwrite,
                IsResumable = isResumable,
                CreatedAt = now,
                LastActivityAt = now,
            };

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                // Refuse up front what finishing would refuse anyway, before any byte is sent. Finishing
                // checks again, since the shelf can change while the bytes arrive.
                await EnsureTargetAcceptsAsync(shelf.Id, normalizedPath, overwrite, session);

                Shelf current = await _shelfRepository.GetByIdAsync(shelf.Id, session)
                    ?? throw new NotFoundException($"There is no shelf '{shelf.Name}'.");

                if (current.QuotaBytes != null)
                {
                    ShelfUsage usage = await _shelfRepository.GetUsageAsync(shelf.Id, session);
                    long free = current.QuotaBytes.Value - usage.TotalBytes;
                    if (sizeBytes > free)
                    {
                        throw new QuotaExceededException(
                            $"The shelf '{shelf.Name}' has {Math.Max(free, 0)} bytes free of its {current.QuotaBytes.Value}, too few for {sizeBytes}.");
                    }
                }

                await _uploadRepository.InsertAsync(upload, session);
                await session.CommitAsync();
            }

            _stagingArea.EnsureExists(upload.Id);
            return upload;
        }

        /// <summary>
        /// Hashes the staged bytes, then in one transaction creates missing directories, moves a replaced
        /// file to the trash, inserts the file row and removes the upload, placing the object last before
        /// the commit. The object's lock is held from placing it until the row commits, so no removal can
        /// take it away in between. Hold the upload's lock.
        /// </summary>
        private async Task<ShelfFile> FinishAsync(UploadRecord upload, CancellationToken cancellationToken)
        {
            string sha256 = await _stagingArea.ComputeSha256Async(upload.Id, cancellationToken);

            using (await _objectStore.LockAsync(sha256, CancellationToken.None))
            {
                bool placed = false;
                try
                {
                    await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                    {
                        await EnsureTargetAcceptsAsync(upload.ShelfId, upload.Path, upload.Overwrite, session);

                        string directoryPath = ShelfPath.GetParent(upload.Path);
                        string name = ShelfPath.GetName(upload.Path);
                        DateTimeOffset now = _timeProvider.GetUtcNow();
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
                        };

                        await _fileRepository.InsertAsync(file, session);
                        await _uploadRepository.DeleteAsync(upload.Id, session);

                        _objectStore.Place(_stagingArea.GetPath(upload.Id), sha256);
                        placed = true;
                        await session.CommitAsync();

                        return StorageContracts.ToShelfFile(file, directoryPath);
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
        }

        /// <summary>Throws unless a file may be stored at <paramref name="normalizedPath"/> as things stand.</summary>
        private async Task EnsureTargetAcceptsAsync(Guid shelfId, string normalizedPath, bool overwrite, IDbSession session)
        {
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
