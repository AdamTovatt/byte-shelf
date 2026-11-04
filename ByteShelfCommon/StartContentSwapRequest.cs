namespace ByteShelfCommon
{
    /// <summary>
    /// Starts a resumable upload that swaps a file's contents in place: encrypting a plain file, or
    /// decrypting an encrypted one, as a client does. The file keeps its id, path, type and description;
    /// its old contents go at once, never to the trash, unless another file holds the same ones. Only
    /// while its contents are still the ones the client read (<see cref="Sha256"/>), so a change made
    /// since is never lost.
    /// </summary>
    public sealed class StartContentSwapRequest
    {
        /// <summary>The file whose contents are swapped.</summary>
        public required Guid FileId { get; init; }

        /// <summary>The SHA-256 its contents must still have when the swap finishes, as the client read it.</summary>
        public required string Sha256 { get; init; }

        /// <summary>The exact size of the new contents in bytes.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>
        /// Whether the new contents are an age file encrypted with a password (<see cref="FileEncryption"/>);
        /// the opposite of what the file is now, since a swap encrypts or decrypts it.
        /// </summary>
        public required bool Encrypted { get; init; }
    }
}
