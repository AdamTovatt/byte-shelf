namespace ByteShelf.Configuration
{
    /// <summary>
    /// Configuration settings for file chunking behavior.
    /// </summary>
    /// <remarks>
    /// This class contains the settings that control how files are split into chunks for storage.
    /// The settings can be configured through the "ChunkConfiguration" section in appsettings.json
    /// or via environment variables.
    /// </remarks>
    public class ChunkConfiguration
    {
        /// <summary>
        /// Gets or sets the size of each chunk in bytes.
        /// </summary>
        /// <remarks>
        /// Files larger than this size will be automatically split into multiple chunks.
        /// Smaller files will be stored as a single chunk. The default value is 1MB (1,048,576 bytes).
        /// Choose a chunk size that balances memory usage, network efficiency, and storage overhead.
        /// </remarks>
        public int ChunkSizeBytes { get; set; } = 1048576; // 1MB default
    }
} 