using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// The order names and paths are listed in: by Unicode code point, which is the order of their UTF-8
    /// bytes and so the order the server's database sorts them in. <see cref="StringComparer.Ordinal"/>
    /// compares UTF-16 units instead, and puts a character above U+FFFF before one in U+E000 to U+FFFF.
    /// </summary>
    public sealed class StoredOrder : IComparer<string>
    {
        /// <summary>The comparer; it holds no state.</summary>
        public static readonly StoredOrder Instance = new StoredOrder();

        private StoredOrder()
        {
        }

        /// <summary>Compares two strings by code point; null comes first.</summary>
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
