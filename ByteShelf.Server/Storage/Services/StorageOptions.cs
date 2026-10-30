namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The storage domain's tunables, read once at startup.</summary>
    public class StorageOptions
    {
        public const int DefaultRestoreWindowDays = 30;

        /// <summary>How long a deletion can be restored before it is purged.</summary>
        public TimeSpan RestoreWindow { get; init; } = TimeSpan.FromDays(DefaultRestoreWindowDays);

        /// <summary>How long a resumable upload may go without a chunk before it is removed.</summary>
        public TimeSpan AbandonedUploadAge { get; init; } = TimeSpan.FromHours(24);

        /// <summary>How often the purge and sweep run.</summary>
        public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromHours(1);
    }
}
