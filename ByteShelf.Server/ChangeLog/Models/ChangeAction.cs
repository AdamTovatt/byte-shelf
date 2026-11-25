namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>
    /// What was done to an entity, as stored in <c>change_log.action</c>. A replica applies a change from
    /// the entity's state in the payload, not from the action, and logs it with the primary's action, so a
    /// <see cref="Released"/> stays one in its own log; live updates tell a removal (<see cref="Purged"/>)
    /// from an entry whose state the log forgot by it (ChangeLogInvalidations).
    /// </summary>
    public static class ChangeAction
    {
        public const string Added = "added";
        public const string Updated = "updated";
        public const string Moved = "moved";
        public const string Deleted = "deleted";
        public const string Restored = "restored";
        public const string Purged = "purged";

        /// <summary>A file's contents swapped in place, encrypting or decrypting it; the old ones are gone.</summary>
        public const string Swapped = "swapped";

        /// <summary>
        /// What was read from contents removed as a file's contents were swapped away from them, which no other
        /// plain file holds: a removal, as <see cref="Purged"/> is, and applied as one, which compaction drops
        /// sooner (<see cref="ChangeLogRetention.PlainTraces"/>).
        /// </summary>
        public const string Released = "released";

        /// <summary>Every action this build logs. A replica stops at one it does not know rather than skip it.</summary>
        public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            Added, Updated, Moved, Deleted, Restored, Purged, Swapped, Released,
        };
    }
}
