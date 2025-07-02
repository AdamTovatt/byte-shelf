namespace ByteShelfCommon
{
    /// <summary>
    /// Result of a quota check operation.
    /// </summary>
    public class QuotaCheckResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QuotaCheckResult"/> class.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileSizeBytes">The file size in bytes that was checked.</param>
        /// <param name="canStore">Whether the file can be stored.</param>
        /// <param name="currentUsageBytes">The current storage usage in bytes.</param>
        /// <param name="storageLimitBytes">The storage limit in bytes.</param>
        /// <param name="availableSpaceBytes">The available space in bytes.</param>
        /// <param name="wouldExceedQuota">Whether storing the file would exceed the quota.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null.</exception>
        public QuotaCheckResult(
            string tenantId,
            long fileSizeBytes,
            bool canStore,
            long currentUsageBytes,
            long storageLimitBytes,
            long availableSpaceBytes,
            bool wouldExceedQuota)
        {
            TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            FileSizeBytes = fileSizeBytes;
            CanStore = canStore;
            CurrentUsageBytes = currentUsageBytes;
            StorageLimitBytes = storageLimitBytes;
            AvailableSpaceBytes = availableSpaceBytes;
            WouldExceedQuota = wouldExceedQuota;
        }

        /// <summary>
        /// Gets the tenant ID.
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Gets the file size in bytes that was checked.
        /// </summary>
        public long FileSizeBytes { get; }

        /// <summary>
        /// Gets whether the file can be stored.
        /// </summary>
        public bool CanStore { get; }

        /// <summary>
        /// Gets the current storage usage in bytes.
        /// </summary>
        public long CurrentUsageBytes { get; }

        /// <summary>
        /// Gets the storage limit in bytes.
        /// </summary>
        public long StorageLimitBytes { get; }

        /// <summary>
        /// Gets the available space in bytes.
        /// </summary>
        public long AvailableSpaceBytes { get; }

        /// <summary>
        /// Gets whether storing the file would exceed the quota.
        /// </summary>
        public bool WouldExceedQuota { get; }
    }
} 