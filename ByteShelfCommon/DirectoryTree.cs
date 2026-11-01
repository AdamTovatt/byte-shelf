namespace ByteShelfCommon
{
    /// <summary>One page of a directory's tree: what lies under it, by path, as <see cref="TreeQuery"/> asked.</summary>
    public sealed class DirectoryTree
    {
        /// <summary>The directory whose tree this is.</summary>
        public required ShelfDirectory Directory { get; init; }

        /// <summary>The page's directories, by path. <see cref="InOrder"/> merges them with <see cref="Files"/>.</summary>
        public required IReadOnlyList<ShelfDirectory> Directories { get; init; }

        /// <summary>The page's files, by path.</summary>
        public required IReadOnlyList<ShelfFile> Files { get; init; }

        /// <summary>What to pass as <see cref="TreeQuery.After"/> for the next page; null when this is the last.</summary>
        public string? NextCursor { get; init; }

        /// <summary>
        /// The page of the first <paramref name="limit"/> entries, directories and files together by path,
        /// with a cursor when more follow. Give it the entries after the previous page's cursor: all of
        /// them, or at least the first <paramref name="limit"/> + 1 of each kind.
        /// </summary>
        public static DirectoryTree Page(ShelfDirectory directory, IEnumerable<ShelfDirectory> directories, IEnumerable<ShelfFile> files, int limit)
        {
            List<TreeEntry> entries = Merge(directories, files).Take(limit + 1).ToList();
            List<TreeEntry> page = entries.Take(limit).ToList();

            return new DirectoryTree
            {
                Directory = directory,
                Directories = page.Where(entry => entry.Directory != null).Select(entry => entry.Directory!).ToList(),
                Files = page.Where(entry => entry.File != null).Select(entry => entry.File!).ToList(),
                NextCursor = entries.Count > limit ? page[^1].Path : null,
            };
        }

        /// <summary>The page's directories and files together, in the order the page holds them.</summary>
        public IReadOnlyList<TreeEntry> InOrder()
        {
            return Merge(Directories, Files).ToList();
        }

        private static IEnumerable<TreeEntry> Merge(IEnumerable<ShelfDirectory> directories, IEnumerable<ShelfFile> files)
        {
            return directories.Select(TreeEntry.Of).Concat(files.Select(TreeEntry.Of)).OrderBy(entry => entry.Path, StoredOrder.Instance);
        }
    }

    /// <summary>One entry of a <see cref="DirectoryTree"/>: a directory or a file.</summary>
    public sealed class TreeEntry
    {
        private TreeEntry(ShelfDirectory? directory, ShelfFile? file)
        {
            Directory = directory;
            File = file;
        }

        /// <summary>The directory, when the entry is one.</summary>
        public ShelfDirectory? Directory { get; }

        /// <summary>The file, when the entry is one.</summary>
        public ShelfFile? File { get; }

        /// <summary>The entry's full path.</summary>
        public string Path => Directory?.Path ?? File!.Path;

        /// <summary>The entry for a directory.</summary>
        public static TreeEntry Of(ShelfDirectory directory)
        {
            return new TreeEntry(directory, null);
        }

        /// <summary>The entry for a file.</summary>
        public static TreeEntry Of(ShelfFile file)
        {
            return new TreeEntry(null, file);
        }
    }
}
