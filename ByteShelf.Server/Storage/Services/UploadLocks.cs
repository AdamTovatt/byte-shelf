using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// One lock per upload, held while its bytes or its row change, so two chunks never write one staging
    /// file at once and the sweep never removes an upload a request is finishing.
    /// </summary>
    public sealed class UploadLocks
    {
        private readonly KeyedAsyncLock _locks = new KeyedAsyncLock();

        /// <summary>Has <paramref name="callback"/> called with an upload's id whenever a caller is about to wait for its lock; for tests that order a race.</summary>
        internal void OnWaitingStarted(Action<Guid> callback)
        {
            _locks.WaitingStarted = key => callback(Guid.ParseExact(key, "D"));
        }

        public Task<IDisposable> AcquireAsync(Guid uploadId, CancellationToken cancellationToken = default)
        {
            return _locks.AcquireAsync(uploadId.ToString("D"), cancellationToken);
        }
    }
}
