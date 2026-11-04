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
        public const string ShelfGrant = "shelf_grant";
        public const string ShelfKey = "shelf_key";
    }
}
