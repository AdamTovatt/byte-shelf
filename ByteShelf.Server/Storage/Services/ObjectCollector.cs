using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The one place an object is removed, and only when no file row, live or trashed, points at it; what else
    /// the server has of it (<see cref="IContentsTraces"/>) goes no later. The check and the removal run under
    /// the object's lock, which a finishing upload also holds from placing the object until its row commits, so
    /// no upload can point a new row at an object between the check and the removal.
    /// </summary>
    public class ObjectCollector
    {
        private readonly IFileRepository _fileRepository;
        private readonly ObjectStore _objectStore;
        private readonly IContentsTraces _traces;
        private readonly ILogger<ObjectCollector> _logger;

        public ObjectCollector(IFileRepository fileRepository, ObjectStore objectStore, IContentsTraces traces, ILogger<ObjectCollector> logger)
        {
            _fileRepository = fileRepository;
            _objectStore = objectStore;
            _traces = traces;
            _logger = logger;
        }

        /// <summary>
        /// For the contents a <see cref="ContentsSession"/> released, once it has committed, which removed what else
        /// the server had of them: removes each object unless a row points at it. What was committed stands
        /// either way: a removal that fails is logged, and maintenance removes it on its next pass, as it does
        /// whatever nothing points at.
        /// </summary>
        public async Task RemoveReleasedAsync(IEnumerable<string> released, CancellationToken cancellationToken = default)
        {
            foreach (string sha256 in released)
            {
                try
                {
                    using (await _objectStore.LockAsync(sha256, cancellationToken))
                    {
                        if (await IsUnreferencedAsync(sha256))
                        {
                            _objectStore.Delete(sha256);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
                {
                    _logger.LogWarning(exception, "Could not remove the released contents {Sha256}; maintenance removes them on its next pass.", sha256);
                }
            }
        }

        /// <summary>Removes the object unless a row points at it; answers whether it was removed.</summary>
        public async Task<bool> RemoveIfUnreferencedAsync(string sha256, CancellationToken cancellationToken = default)
        {
            using (await _objectStore.LockAsync(sha256, cancellationToken))
            {
                if (!await IsUnreferencedAsync(sha256))
                {
                    return false;
                }

                await _traces.RemoveUnheldAsync(sha256, cancellationToken);
                _objectStore.Delete(sha256);
                return true;
            }
        }

        private async Task<bool> IsUnreferencedAsync(string sha256)
        {
            return _objectStore.Exists(sha256) && !await _fileRepository.IsObjectReferencedAsync(sha256);
        }
    }
}
