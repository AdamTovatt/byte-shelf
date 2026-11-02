namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>
    /// What was done to an entity, as stored in <c>change_log.action</c>. Descriptive only: a replica
    /// applies a change from the entity's state in the payload, never from the action.
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
    }
}
