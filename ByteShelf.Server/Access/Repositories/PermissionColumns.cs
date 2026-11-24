using ByteShelfCommon;

namespace ByteShelf.Server.Access.Repositories
{
    /// <summary>How the permission enums are stored: the text the migration's CHECK constraints allow.</summary>
    internal static class PermissionColumns
    {
        public static string ToColumn(ShelfAccessPermission permission)
        {
            return permission switch
            {
                ShelfAccessPermission.Read => "read",
                ShelfAccessPermission.ReadWrite => "read_write",
                _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "Not a shelf access permission."),
            };
        }

        public static ShelfAccessPermission ToAccessPermission(string column)
        {
            return column switch
            {
                "read" => ShelfAccessPermission.Read,
                "read_write" => ShelfAccessPermission.ReadWrite,
                _ => throw new InvalidOperationException($"'{column}' is not a stored shelf access permission."),
            };
        }

        public static string ToColumn(ShelfPermission permission)
        {
            return permission switch
            {
                ShelfPermission.Read => "read",
                ShelfPermission.AddOnly => "add_only",
                ShelfPermission.ReadWrite => "read_write",
                _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "Not a shelf key permission."),
            };
        }

        public static ShelfPermission ToKeyPermission(string column)
        {
            return column switch
            {
                "read" => ShelfPermission.Read,
                "add_only" => ShelfPermission.AddOnly,
                "read_write" => ShelfPermission.ReadWrite,
                _ => throw new InvalidOperationException($"'{column}' is not a stored shelf key permission."),
            };
        }
    }
}
