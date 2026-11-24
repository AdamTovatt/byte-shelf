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
        public const string ShelfKeyGrant = "shelf_key_grant";

        /// <summary>What was read from an object's contents, named by <see cref="Metadata.Models.ObjectMetadata.IdFor"/>.</summary>
        public const string ObjectMetadata = "object_metadata";

        /// <summary>What a plugin's reader found in an object's contents, named by <see cref="Metadata.Models.ContentPluginMetadata.IdFor"/>.</summary>
        public const string ContentPluginMetadata = "content_plugin_metadata";

        /// <summary>What a plugin keeps about a file, named by <see cref="Metadata.Models.FilePluginMetadata.IdFor"/>.</summary>
        public const string FilePluginMetadata = "file_plugin_metadata";

        /// <summary>The entity types whose rows go with another's unlogged, each with what it goes with.</summary>
        public static IReadOnlyDictionary<string, EntityOwner> Owned { get; } = new Dictionary<string, EntityOwner>
        {
            [FilePluginMetadata] = new EntityOwner(File, "file_plugin_metadata", "file_id"),
        };

        /// <summary>What rows of <paramref name="entityType"/> go with unlogged; null for most, whose every removal is logged.</summary>
        public static EntityOwner? OwnerOf(string entityType)
        {
            return Owned.GetValueOrDefault(entityType);
        }

        /// <summary>
        /// Whether the log forgets an entity of <paramref name="entityType"/>'s earlier states once it is
        /// removed. True for what was read from contents: a photo encrypted in place loses it at once, and
        /// its position, camera and title must not outlive it in the log for the compaction window.
        /// </summary>
        public static bool ForgetsRemovedStates(string entityType)
        {
            return entityType is ObjectMetadata or ContentPluginMetadata;
        }
    }
}
