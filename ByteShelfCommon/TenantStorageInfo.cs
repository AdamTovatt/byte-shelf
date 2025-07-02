namespace ByteShelfCommon
{
    /// <summary>
    /// Information about a tenant's storage usage and limits.
    /// </summary>
    public class TenantStorageInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantStorageInfo"/> class.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="currentUsageBytes">The current storage usage in bytes.</param>
        /// <param name="storageLimitBytes">The storage limit in bytes.</param>
        /// <param name="availableSpaceBytes">The available space in bytes.</param>
        /// <param name="usagePercentage">The usage percentage (0-100).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null.</exception>
        public TenantStorageInfo(
            string tenantId,
            long currentUsageBytes,
            long storageLimitBytes,
            long availableSpaceBytes,
            double usagePercentage)
        {
            TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            CurrentUsageBytes = currentUsageBytes;
            StorageLimitBytes = storageLimitBytes;
            AvailableSpaceBytes = availableSpaceBytes;
            UsagePercentage = usagePercentage;
        }

        /// <summary>
        /// Gets the tenant ID.
        /// </summary>
        public string TenantId { get; }

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
        /// Gets the usage percentage (0-100).
        /// </summary>
        public double UsagePercentage { get; }
    }
} 