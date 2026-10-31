namespace ByteShelfCommon
{
    /// <summary>Creates a directory, and any missing directory above it.</summary>
    public sealed class CreateDirectoryRequest
    {
        /// <summary>The directory's full path.</summary>
        public required string Path { get; init; }
    }
}
