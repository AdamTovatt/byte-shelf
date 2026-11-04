using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The one place an object is removed: only when no file row, live or trashed, points at it. The
    /// check and the removal run under the object's lock, which a finishing upload also holds from
    /// placing the object until its row commits, so no upload can point a new row at an object between
    /// the check and the removal.
    /// </summary>
    public class ObjectCollector
    {
        private readonly IFileRepository _fileRepository;
        private readonly ObjectStore _objectStore;
        private readonly ILogger<ObjectCollector> _logger;

        public ObjectCollector(IFileRepository fileRepository, ObjectStore objectStore, ILogger<ObjectCollector> logger)
        {
            _fileRepository = fileRepository;
            _objectStore = objectStore;
            _logger = logger;
        }

        /// <summary>
        /// Removes the old contents of files whose contents were swapped in place, once the swap has
        /// committed, unless a row still points at them. The swap stands either way: a removal that fails
        /// is logged, and maintenance removes the object on its next pass, as it does whatever nothing
        /// points at.
        /// </summary>
        public async Task RemoveReleasedAsync(IEnumerable<string> released)
        {
            foreach (string sha256 in released)
            {
                try
                {
                    await RemoveIfUnreferencedAsync(sha256, CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
                {
                    _logger.LogWarning(exception, "Could not remove the swapped-out contents {Sha256}; maintenance removes them on its next pass.", sha256);
                }
            }
        }

        /// <summary>Removes the object unless a row points at it; answers whether it was removed.</summary>
        public async Task<bool> RemoveIfUnreferencedAsync(string sha256, CancellationToken cancellationToken = default)
        {
            using (await _objectStore.LockAsync(sha256, cancellationToken))
            {
                if (!_objectStore.Exists(sha256) || await _fileRepository.IsObjectReferencedAsync(sha256))
                {
                    return false;
                }

                _objectStore.Delete(sha256);
                return true;
            }
        }
    }
}
