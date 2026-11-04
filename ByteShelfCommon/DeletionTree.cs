namespace ByteShelfCommon
{
    /// <summary>
    /// One page of what a deletion took, by the paths it had, without restoring it: what the deletion
    /// was, the password its encrypted files open with, and the page.
    /// </summary>
    public sealed class DeletionTree
    {
        /// <summary>The deletion as the trash lists it: whether it took a file or a directory, and which.</summary>
        public required TrashEntry Deletion { get; init; }

        /// <summary>
        /// The encrypted folder the entry at the path asked for (the deleted entry, when none was) was in, or
        /// was itself, whose password its encrypted files open with; null when none. It may have been moved
        /// or deleted since, and its check still tells its password; outside one, each encrypted file has
        /// its own.
        /// </summary>
        public EncryptedFolder? EncryptedBy { get; init; }

        /// <summary>
        /// The page: its directory is the one the deletion was made in, and its entries are what the
        /// deletion took at the path asked for and under it (all it took, when none was), that path's
        /// entry first.
        /// </summary>
        public required DirectoryTree Tree { get; init; }
    }
}
