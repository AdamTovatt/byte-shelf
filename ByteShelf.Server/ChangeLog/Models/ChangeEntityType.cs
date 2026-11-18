namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>The kinds of entity the change log records, as stored in <c>change_log.entity_type</c>.</summary>
    public static class ChangeEntityType
    {
        public const string Shelf = "shelf";
        public const string Directory = "directory";
        public const string File = "file";
        public const string User = "user";
        public const string UserLogin = "user_login";
        public const string Passkey = "passkey";
        public const string ShelfGrant = "shelf_grant";
        public const string ShelfKey = "shelf_key";

        /// <summary>What was read from an object's contents, named by <see cref="Metadata.Models.ObjectMetadata.IdFor"/>.</summary>
        public const string ObjectMetadata = "object_metadata";

        /// <summary>
        /// Whether the log forgets an entity of <paramref name="entityType"/>'s earlier states once it is
        /// removed. True for <see cref="ObjectMetadata"/>: a photo encrypted in place loses its row at once,
        /// and its position, camera and title must not outlive it in the log for the compaction window.
        /// </summary>
        public static bool ForgetsRemovedStates(string entityType)
        {
            return entityType == ObjectMetadata;
        }
    }
}
