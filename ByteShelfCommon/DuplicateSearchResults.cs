using System.ComponentModel.DataAnnotations;

namespace ByteShelfCommon
{
    /// <summary>One page of a <see cref="DuplicateSearch"/>'s groups, most repeated bytes first.</summary>
    public sealed class DuplicateSearchResults
    {
        /// <summary>The groups, most <see cref="DuplicateGroup.RepeatedBytes"/> first, then by SHA-256.</summary>
        public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

        /// <summary>What to pass as <see cref="DuplicateSearch.After"/> for the next page; null on the last one.</summary>
        public DuplicateCursor? NextCursor { get; init; }

        /// <summary>
        /// The page of the first <paramref name="limit"/> groups after <paramref name="after"/> that the
        /// files make, with a cursor when more follow. Give it the files a <see cref="DuplicateSearch"/>
        /// takes: all of them, or every file of at least the first <paramref name="limit"/> + 1 groups
        /// after the cursor.
        /// </summary>
        public static DuplicateSearchResults Page(IEnumerable<ShelfFile> files, DuplicateCursor? after, int limit)
        {
            List<DuplicateGroup> groups = files
                .GroupBy(file => file.Sha256, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateGroup
                {
                    Sha256 = group.Key,
                    SizeBytes = group.First().SizeBytes,
                    Files = group.OrderBy(file => file.Path, StoredOrder.Instance).ToList(),
                })
                .Where(group => after == null || after.Value.Precedes(group))
                .OrderByDescending(group => group.RepeatedBytes)
                .ThenBy(group => group.Sha256, StringComparer.Ordinal)
                .Take(limit + 1)
                .ToList();

            List<DuplicateGroup> page = groups.Take(limit).ToList();
            return new DuplicateSearchResults
            {
                Groups = page,
                NextCursor = groups.Count > limit ? DuplicateCursor.Of(page[^1]) : null,
            };
        }
    }

    /// <summary>Files with the same contents: two or more, by path.</summary>
    public sealed class DuplicateGroup
    {
        /// <summary>The contents' SHA-256, in lowercase hex.</summary>
        public required string Sha256 { get; init; }

        /// <summary>The size of each file.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>The files, by path.</summary>
        public required IReadOnlyList<ShelfFile> Files { get; init; }

        /// <summary>The bytes the copies after the first hold: what removing them would tidy away.</summary>
        /// <remarks>Always sent, so the schema marks it required, as it does the <c>required</c> members.</remarks>
        [Required]
        public long RepeatedBytes => SizeBytes * (Files.Count - 1);
    }
}
