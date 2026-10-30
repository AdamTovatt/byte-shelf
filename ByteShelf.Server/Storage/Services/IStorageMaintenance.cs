using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The cleanup that keeps the root holding only what the database points at.</summary>
    public interface IStorageMaintenance
    {
        /// <summary>
        /// One pass: removes uploads with no activity for the abandoned-upload age, single-PUT uploads
        /// started before <paramref name="processStartedAt"/> (their request did not survive the restart),
        /// staging entries with no upload, the trash past its restore window, and objects no row points at.
        /// </summary>
        Task<StorageMaintenanceSummary> RunAsync(DateTimeOffset processStartedAt, CancellationToken cancellationToken);
    }
}
