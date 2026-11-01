using System.Text;

namespace ByteShelfClient.InMemory
{
    /// <summary>
    /// Names and paths in the order the server's SQLite sorts them: by their UTF-8 bytes, which is
    /// code point order. <see cref="StringComparer.Ordinal"/> compares UTF-16 units instead, and puts a
    /// character above U+FFFF before one in U+E000 to U+FFFF.
    /// </summary>
    internal sealed class StoredOrder : IComparer<string>
    {
        public static readonly StoredOrder Instance = new StoredOrder();

        public int Compare(string? x, string? y)
        {
            if (x == null || y == null)
            {
                return x == null ? (y == null ? 0 : -1) : 1;
            }

            StringRuneEnumerator left = x.EnumerateRunes();
            StringRuneEnumerator right = y.EnumerateRunes();
            while (true)
            {
                bool hasLeft = left.MoveNext();
                bool hasRight = right.MoveNext();
                if (!hasLeft || !hasRight)
                {
                    return hasLeft.CompareTo(hasRight);
                }

                int compared = left.Current.Value.CompareTo(right.Current.Value);
                if (compared != 0)
                {
                    return compared;
                }
            }
        }
    }
}
