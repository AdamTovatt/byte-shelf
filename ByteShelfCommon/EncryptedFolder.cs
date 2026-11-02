namespace ByteShelfCommon
{
    /// <summary>A directory encrypted with a password, with what tells the password apart.</summary>
    public sealed class EncryptedFolder
    {
        /// <summary>The encrypted directory.</summary>
        public required ShelfDirectory Directory { get; init; }

        /// <summary>
        /// Base64 of an age file of <see cref="FileEncryption.CheckText"/> encrypted with the directory's
        /// password: a password that decrypts it to that text is the directory's.
        /// </summary>
        public required string Check { get; init; }
    }

    /// <summary>What a path on a shelf is encrypted under, as <c>GET encryption</c> answers it.</summary>
    public sealed class PathEncryption
    {
        /// <summary>
        /// The encrypted directory a file stored at the path would be under (the path itself, when it is
        /// one), whose password it must be encrypted with; null when there is none.
        /// </summary>
        public EncryptedFolder? EncryptedBy { get; init; }
    }

    /// <summary>Encrypts a directory: from now on every file stored under it must be encrypted with its password.</summary>
    public sealed class SetFolderEncryptionRequest
    {
        /// <summary>Base64 of an age file of <see cref="FileEncryption.CheckText"/> encrypted with the password.</summary>
        public required string Check { get; init; }
    }
}
