using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>The words for access and key permissions, read and printed the same way.</summary>
    internal static class PermissionNames
    {
        private static readonly Dictionary<string, ShelfAccessPermission> Access = new Dictionary<string, ShelfAccessPermission>(StringComparer.Ordinal)
        {
            ["read"] = ShelfAccessPermission.Read,
            ["read-write"] = ShelfAccessPermission.ReadWrite,
        };

        private static readonly Dictionary<string, ShelfKeyPermission> Key = new Dictionary<string, ShelfKeyPermission>(StringComparer.Ordinal)
        {
            ["read"] = ShelfKeyPermission.Read,
            ["add-only"] = ShelfKeyPermission.AddOnly,
            ["read-write"] = ShelfKeyPermission.ReadWrite,
        };

        public static string AccessWords => string.Join("|", Access.Keys);

        public static string KeyWords => string.Join("|", Key.Keys);

        public static string Of(ShelfAccessPermission permission)
        {
            return Access.First(pair => pair.Value == permission).Key;
        }

        public static string Of(ShelfKeyPermission permission)
        {
            return Key.First(pair => pair.Value == permission).Key;
        }

        /// <exception cref="UsageException">It is not one of <see cref="AccessWords"/>.</exception>
        public static ShelfAccessPermission ParseAccess(string word)
        {
            return Access.TryGetValue(word, out ShelfAccessPermission permission)
                ? permission
                : throw new UsageException($"'{word}' is not an access; give {AccessWords}.");
        }

        /// <exception cref="UsageException">It is not one of <see cref="KeyWords"/>.</exception>
        public static ShelfKeyPermission ParseKey(string word)
        {
            return Key.TryGetValue(word, out ShelfKeyPermission permission)
                ? permission
                : throw new UsageException($"'{word}' is not a key's access; give {KeyWords}.");
        }
    }
}
