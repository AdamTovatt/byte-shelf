using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>The words for permissions, read and printed the same way.</summary>
    internal static class PermissionNames
    {
        private static readonly Dictionary<string, ShelfAccessPermission> Access = new Dictionary<string, ShelfAccessPermission>(StringComparer.Ordinal)
        {
            ["read"] = ShelfAccessPermission.Read,
            ["read-write"] = ShelfAccessPermission.ReadWrite,
        };

        private static readonly Dictionary<string, ShelfPermission> Key = new Dictionary<string, ShelfPermission>(StringComparer.Ordinal)
        {
            ["read"] = ShelfPermission.Read,
            ["add-only"] = ShelfPermission.AddOnly,
            ["read-write"] = ShelfPermission.ReadWrite,
        };

        public static string AccessWords => string.Join("|", Access.Keys);

        public static string KeyWords => string.Join("|", Key.Keys);

        public static string Of(ShelfAccessPermission permission)
        {
            return Access.First(pair => pair.Value == permission).Key;
        }

        public static string Of(ShelfPermission permission)
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
        public static ShelfPermission ParseKey(string word)
        {
            return Key.TryGetValue(word, out ShelfPermission permission)
                ? permission
                : throw new UsageException($"'{word}' is not a key's access; give {KeyWords}.");
        }
    }
}
