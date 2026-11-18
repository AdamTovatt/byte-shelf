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

        /// <summary>
        /// Taken at or after this, by the time a photo's or video's contents carry (<see cref="FileMetadata.TakenAt"/>).
        /// This and the other metadata criteria match only files the server has read, so never in the
        /// in-memory client, which reads none.
        /// </summary>
        public DateTimeOffset? TakenFrom { get; init; }

        /// <summary>Taken before this.</summary>
        public DateTimeOffset? TakenBefore { get; init; }

        /// <summary>Only files whose contents carry a GPS position; false leaves it unchecked.</summary>
        public bool HasGps { get; init; }

        /// <summary>The fewest seconds a video plays (<see cref="FileMetadata.DurationSeconds"/>).</summary>
        public double? MinDurationSeconds { get; init; }

        /// <summary>The most seconds a video plays (<see cref="FileMetadata.DurationSeconds"/>).</summary>
        public double? MaxDurationSeconds { get; init; }

        /// <summary>The fewest pages a PDF has.</summary>
        public int? MinPageCount { get; init; }

        /// <summary>The most pages a PDF has.</summary>
        public int? MaxPageCount { get; init; }

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

            if (TakenFrom >= TakenBefore)
            {
                throw new ArgumentException("A time range ends before it starts.");
            }

            ValidateMetadataCriteria();

            return new FileSearch(NormalizeCriteria())
            {
                TakenFrom = TakenFrom,
                TakenBefore = TakenBefore,
                HasGps = HasGps,
                MinDurationSeconds = MinDurationSeconds,
                MaxDurationSeconds = MaxDurationSeconds,
                MinPageCount = MinPageCount,
                MaxPageCount = MaxPageCount,
                Limit = limit,
                After = string.IsNullOrEmpty(After) ? null : After,
            };
        }

        /// <summary>
        /// Whether any criterion asks about what a file's contents carry (<see cref="FileMetadata"/>),
        /// which only files the server has read can meet.
        /// </summary>
        public bool AsksForMetadata()
        {
            return TakenFrom != null || TakenBefore != null || HasGps
                || MinDurationSeconds != null || MaxDurationSeconds != null
                || MinPageCount != null || MaxPageCount != null;
        }

        /// <exception cref="ArgumentException">A duration or page count is not allowed, or its range is empty.</exception>
        private void ValidateMetadataCriteria()
        {
            if (MinDurationSeconds < 0 || MaxDurationSeconds < 0 || MinPageCount < 0 || MaxPageCount < 0)
            {
                throw new ArgumentException("A duration or page count cannot be negative.");
            }

            if (!double.IsFinite(MinDurationSeconds ?? 0) || !double.IsFinite(MaxDurationSeconds ?? 0))
            {
                throw new ArgumentException("A duration is a number of seconds.");
            }

            if (MinDurationSeconds > MaxDurationSeconds || MinPageCount > MaxPageCount)
            {
                throw new ArgumentException("The smallest duration or page count is above the largest.");
            }
        }

        /// <summary>The query string carrying every criterion that is set, escaped, without a leading <c>?</c>.</summary>
        public string ToQuery()
        {
            List<string> parts = QueryParts();
            AddQueryPart(parts, "takenFrom", TakenFrom?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "takenBefore", TakenBefore?.ToString("O", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "hasGps", HasGps ? "true" : null);
            AddQueryPart(parts, "minDurationSeconds", MinDurationSeconds?.ToString("R", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "maxDurationSeconds", MaxDurationSeconds?.ToString("R", CultureInfo.InvariantCulture));
            AddQueryPart(parts, "minPageCount", MinPageCount?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "maxPageCount", MaxPageCount?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "limit", Limit?.ToString(CultureInfo.InvariantCulture));
            AddQueryPart(parts, "after", After);
            return string.Join("&", parts);
        }
    }
}
