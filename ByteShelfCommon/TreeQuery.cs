using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>
    /// What to list of a directory's tree: the live directories and files under it, at any depth or
    /// down to <see cref="Depth"/>, together by path (<see cref="StoredOrder"/>) a page at a time. Pass
    /// a page's <see cref="DirectoryTree.NextCursor"/> as <see cref="After"/> for the next one.
    /// </summary>
    public sealed class TreeQuery
    {
        /// <summary>How many entries a page holds when <see cref="Limit"/> is not set.</summary>
        public const int DefaultLimit = 100;

        /// <summary>The most entries one page may hold.</summary>
        public const int MaxLimit = 1000;

        /// <summary>The directory whose tree to list; the shelf's root when null.</summary>
        public string? Under { get; init; }

        /// <summary>How many levels down to list: 1 is what the directory holds directly. Null lists every level.</summary>
        public int? Depth { get; init; }

        /// <summary>How many entries a page holds: 1 to <see cref="MaxLimit"/>, <see cref="DefaultLimit"/> when null.</summary>
        public int? Limit { get; init; }

        /// <summary>The previous page's <see cref="DirectoryTree.NextCursor"/>; null for the first page.</summary>
        public string? After { get; init; }

        /// <summary>The query with its defaults filled in: <see cref="Under"/> normalized and <see cref="Limit"/> set.</summary>
        /// <exception cref="ArgumentException">The limit, the depth or the path is not allowed.</exception>
        public TreeQuery Normalize()
        {
            int limit = Limit ?? DefaultLimit;
            if (limit < 1 || limit > MaxLimit)
            {
                throw new ArgumentException($"A page holds 1 to {MaxLimit} entries, not {limit}.", nameof(Limit));
            }

            if (Depth < 1)
            {
                throw new ArgumentException($"A depth is 1 or more, not {Depth}.", nameof(Depth));
            }

            return new TreeQuery
            {
                Under = Under == null ? ShelfPath.Root : ShelfPath.Normalize(Under),
                Depth = Depth,
                Limit = limit,
                After = string.IsNullOrEmpty(After) ? null : After,
            };
        }

        /// <summary>The query as the server's <c>tree</c> endpoint takes it, without the leading <c>?</c>.</summary>
        public string ToQuery()
        {
            List<string> parts = new List<string>();
            Add(parts, "under", Under);
            Add(parts, "depth", Depth?.ToString(CultureInfo.InvariantCulture));
            Add(parts, "limit", Limit?.ToString(CultureInfo.InvariantCulture));
            Add(parts, "after", After);
            return string.Join("&", parts);
        }

        private static void Add(List<string> parts, string name, string? value)
        {
            if (value != null)
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }
    }
}
