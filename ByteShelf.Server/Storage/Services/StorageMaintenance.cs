using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class StorageMaintenance : IStorageMaintenance
    {
        private readonly IUploadRepository _uploadRepository;
        private readonly ITrashService _trashService;
        private readonly ObjectCollector _objectCollector;
        private readonly StagingArea _stagingArea;
        private readonly ObjectStore _objectStore;
        private readonly UploadLocks _uploadLocks;
        private readonly StorageOptions _options;
        private readonly TimeProvider _timeProvider;

        public StorageMaintenance(
            IUploadRepository uploadRepository,
            ITrashService trashService,
            ObjectCollector objectCollector,
            StagingArea stagingArea,
            ObjectStore objectStore,
            UploadLocks uploadLocks,
            StorageOptions options,
            TimeProvider timeProvider)
        {
            _uploadRepository = uploadRepository;
            _trashService = trashService;
            _objectCollector = objectCollector;
            _stagingArea = stagingArea;
            _objectStore = objectStore;
            _uploadLocks = uploadLocks;
            _options = options;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<StorageMaintenanceSummary> RunAsync(DateTimeOffset processStartedAt, CancellationToken cancellationToken)
        {
            int abandonedUploads = await RemoveAbandonedUploadsAsync(processStartedAt, cancellationToken);
            int strayStagingEntries = await RemoveStrayStagingEntriesAsync();
            int purgedFiles = await _trashService.PurgeExpiredAsync(cancellationToken);
            int removedObjects = await RemoveUnreferencedObjectsAsync(cancellationToken);

            return new StorageMaintenanceSummary(abandonedUploads, strayStagingEntries, purgedFiles, removedObjects);
        }

        private async Task<int> RemoveAbandonedUploadsAsync(DateTimeOffset processStartedAt, CancellationToken cancellationToken)
        {
            DateTimeOffset idleBefore = _timeProvider.GetUtcNow() - _options.AbandonedUploadAge;
            int removed = 0;

            foreach (UploadRecord candidate in await _uploadRepository.GetAbandonedAsync(idleBefore, processStartedAt))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (await _uploadLocks.AcquireAsync(candidate.Id, cancellationToken))
                {
                    // Looked at again under the lock: a chunk may have arrived since the list was read.
                    UploadRecord? upload = await _uploadRepository.GetByIdAsync(candidate.Id);
                    bool stillAbandoned = upload != null
                        && (upload.LastActivityAt < idleBefore || (!upload.IsResumable && upload.CreatedAt < processStartedAt));

                    if (stillAbandoned && await _uploadRepository.DeleteAsync(candidate.Id))
                    {
                        _stagingArea.Delete(candidate.Id);
                        removed++;
                    }
                }
            }

            return removed;
        }

        private async Task<int> RemoveStrayStagingEntriesAsync()
        {
            // Listed before the ids are read: an upload's row commits before its staging file is made,
            // so any entry on this list whose upload is live has its id in the set read after it.
            IReadOnlyList<StagingEntry> entries = _stagingArea.List();
            IReadOnlySet<Guid> uploadIds = await _uploadRepository.GetAllIdsAsync();
            int removed = 0;

            foreach (StagingEntry entry in entries)
            {
                if (entry.UploadId != null && uploadIds.Contains(entry.UploadId.Value))
                {
                    continue;
                }

                _stagingArea.Remove(entry);
                removed++;
            }

            return removed;
        }

        private async Task<int> RemoveUnreferencedObjectsAsync(CancellationToken cancellationToken)
        {
            int removed = 0;

            foreach (string sha256 in _objectStore.EnumerateHashes().ToList())
            {
                if (await _objectCollector.RemoveIfUnreferencedAsync(sha256, cancellationToken))
                {
                    removed++;
                }
            }

            return removed;
        }
    }
}
