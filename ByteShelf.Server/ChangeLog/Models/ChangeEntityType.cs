namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>The kinds of entity the change log records, as stored in <c>change_log.entity_type</c>.</summary>
    public static class ChangeEntityType
    {
        public const string Shelf = "shelf";
        public const string Directory = "directory";
        public const string File = "file";
    }
}
