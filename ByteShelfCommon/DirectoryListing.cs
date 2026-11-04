namespace ByteShelfCommon
{
    /// <summary>What one directory holds: its subdirectories and files, each sorted by name.</summary>
    public sealed class DirectoryListing
    {
        /// <summary>The directory listed.</summary>
        public required ShelfDirectory Directory { get; init; }

        /// <summary>The directories directly inside it, each with the size and count of the files under it.</summary>
        public required IReadOnlyList<ListedDirectory> Directories { get; init; }

        /// <summary>The files directly inside it.</summary>
        public required IReadOnlyList<ShelfFile> Files { get; init; }

        /// <summary>
        /// The encrypted directory the listed one is (or is inside), whose password every file stored
        /// here is encrypted with from its encryption on; null when it is not encrypted.
        /// </summary>
        public EncryptedFolder? EncryptedBy { get; init; }
    }
}
