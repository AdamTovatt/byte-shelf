using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>
    /// The files that meet the <see cref="FileCriteria"/>, by path, a page at a time: pass a page's
    /// <see cref="FileSearchResults.NextCursor"/> as <see cref="After"/> for the next one.
    /// </summary>
    public sealed class FileSearch : FileCriteria
    {
        /// <summary>How many files a page holds when <see cref="Limit"/> is not set.</summary>
        public const int DefaultLimit = 100;

        /// <summary>The most files one page may hold.</summary>
        public const int MaxLimit = 1000;

        /// <summary>A search to set with an object initializer.</summary>
        public FileSearch()
        {
        }

        /// <summary>A search by <paramref name="criteria"/>, as they are; set the paging with an object initializer.</summary>
        public FileSearch(FileCriteria criteria)
            : base(criteria)
        {
        }

        /// <summary>How many files a page holds: 1 to <see cref="MaxLimit"/>, <see cref="DefaultLimit"/> when null.</summary>
        public int? Limit { get; init; }

        /// <summary>The previous page's <see cref="FileSearchResults.NextCursor"/>; null for the first page.</summary>
        public string? After { get; init; }

        /// <summary>
        /// This search with every criterion checked and put the way it is compared, as
        /// <see cref="FileCriteria"/> does, and <see cref="Limit"/> always set.
        /// </summary>
        /// <exception cref="ArgumentException">A criterion or the limit is not allowed, or a range is empty.</exception>
        public FileSearch Normalize()
        {
            int limit = Limit ?? DefaultLimit;
            if (limit < 1 || limit > MaxLimit)
            {
                throw new ArgumentException($"A page holds 1 to {MaxLimit} files, not {limit}.", nameof(Limit));
            }

            return new FileSearch(NormalizeCriteria())
            {
                Limit = limit,
                After = string.IsNullOrEmpty(After) ? null : After,
            };
        }

        /// <summary>The query string carrying every criterion that is set, escaped, without a leading <c>?</c>.</summary>
        public string ToQuery()
        {
            List<string> parts = QueryParts();
            AddQueryPart(parts, "limit", Limit?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "after", After);
            return string.Join("&", parts);
        }
    }
}
