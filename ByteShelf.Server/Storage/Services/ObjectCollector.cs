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

        public ObjectCollector(IFileRepository fileRepository, ObjectStore objectStore)
        {
            _fileRepository = fileRepository;
            _objectStore = objectStore;
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
