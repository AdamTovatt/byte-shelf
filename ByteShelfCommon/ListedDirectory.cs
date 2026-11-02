namespace ByteShelfCommon
{
    /// <summary>A directory as a listing of the directory that holds it shows it: with what is under it.</summary>
    public sealed class ListedDirectory
    {
        /// <summary>The directory.</summary>
        public required ShelfDirectory Directory { get; init; }

        /// <summary>The size of every live file under the directory, at any depth.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>How many live files are under the directory, at any depth.</summary>
        public required int FileCount { get; init; }

        /// <summary>
        /// How many of those files are encrypted, leaving out those under an encrypted directory below
        /// this one, which that directory's own count holds. Below <see cref="FileCount"/> under an
        /// encrypted directory, or above zero under a plain one, it shows encrypting or decrypting in
        /// place left files to finish.
        /// </summary>
        public required int EncryptedFileCount { get; init; }
    }
}
