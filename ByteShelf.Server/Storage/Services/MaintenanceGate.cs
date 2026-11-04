using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Held by storage maintenance for a whole pass, and by anything that stages objects outside an
    /// upload (a replica's sync) from fetching an object until a committed row points at it. So the
    /// sweeps of <c>staging/</c> and of unreferenced objects never take something about to be used.
    /// </summary>
    public sealed class MaintenanceGate
    {
        private const string Key = "maintenance";

        private readonly KeyedAsyncLock _lock = new KeyedAsyncLock();

        /// <summary>Waits for the gate; dispose the answer to release it.</summary>
        public Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            return _lock.AcquireAsync(Key, cancellationToken);
        }
    }
}
