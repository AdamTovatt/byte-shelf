namespace ByteShelfCommon
{
    /// <summary>
    /// Request model for updating a tenant's storage limit.
    /// </summary>
    public class UpdateStorageLimitRequest
    {
        /// <summary>
        /// Gets or sets the new storage limit in bytes.
        /// </summary>
        public long StorageLimitBytes { get; set; }
    }
}