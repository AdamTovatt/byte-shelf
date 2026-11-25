namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>How long compaction keeps change-log entries (ChangeLog.md, <i>Compaction</i>).</summary>
    public static class ChangeLogRetention
    {
        /// <summary>How long every entry is kept, and so how long a replica may fall behind before it needs a copy.</summary>
        public static readonly TimeSpan Entries = TimeSpan.FromDays(30);

        /// <summary>
        /// How long the log keeps what tells of plain contents a file was encrypted away from, counted from when
        /// this server logged the encryption: the file's earlier entries whose state is not encrypted
        /// (<see cref="ChangeEntityType.EncryptedFlag"/>), and the entries of what was read from those contents up
        /// to its removal at the swap (<see cref="ChangeAction.Released"/>). Their SHA-256 and size, and the ids
        /// made from the SHA-256, would let anyone with the database check whether the shelf held a known file.
        /// </summary>
        public static readonly TimeSpan PlainTraces = TimeSpan.FromHours(24);
    }
}
