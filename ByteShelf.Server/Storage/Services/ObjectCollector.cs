using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The one place an object is removed, and only when no file row, live or trashed, points at it; what was
    /// derived from it goes no later. The check and the removal run under the object's lock, which a finishing
    /// upload also holds from placing the object until its row commits, so no upload can point a new row at an
    /// object between the check and the removal.
    /// </summary>
    public class ObjectCollector
    {
        private readonly IFileRepository _fileRepository;
        private readonly ObjectStore _objectStore;
        private readonly IDerivedRemoval _derivedRemoval;
        private readonly ILogger<ObjectCollector> _logger;

        public ObjectCollector(IFileRepository fileRepository, ObjectStore objectStore, IDerivedRemoval derivedRemoval, ILogger<ObjectCollector> logger)
        {
            _fileRepository = fileRepository;
            _objectStore = objectStore;
            _derivedRemoval = derivedRemoval;
            _logger = logger;
        }

        /// <summary>
        /// For contents that just lost a row, swapped in place or purged, once that has committed: removes what was
        /// derived from them unless a plain file holds them still, and the object unless a row points at it. What
        /// was committed stands either way: a removal that fails is logged, and maintenance removes it on its
        /// next pass, as it does whatever nothing points at.
        /// </summary>
        public async Task RemoveReleasedAsync(IEnumerable<string> released, CancellationToken cancellationToken = default)
        {
            foreach (string sha256 in released)
            {
                try
                {
                    using (await _objectStore.LockAsync(sha256, cancellationToken))
                    {
                        // Whether or not the object goes: an encrypted file may hold the same bytes and keep it.
                        await _derivedRemoval.RemoveUnheldAsync(sha256, cancellationToken);
                        if (await IsUnreferencedAsync(sha256))
                        {
                            _objectStore.Delete(sha256);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Data.Common.DbException or PluginNotStoppedException)
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

                await _derivedRemoval.RemoveUnheldAsync(sha256, cancellationToken);
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
