using ByteShelfCommon;

namespace ByteShelf.Cli.Shared
{
    /// <summary>A place on a shelf: written <c>shelf:/path</c> on the command line, or <c>shelf</c> alone for its root.</summary>
    internal sealed class ShelfLocation
    {
        private ShelfLocation(string shelf, string path, bool namesADirectory)
        {
            Shelf = shelf;
            Path = path;
            NamesADirectory = namesADirectory;
        }

        public string Shelf { get; }

        /// <summary>The normalized path.</summary>
        public string Path { get; }

        /// <summary>Whether it was written ending in <c>/</c> (or is the root), so a file put there keeps its own name.</summary>
        public bool NamesADirectory { get; }

        /// <exception cref="UsageException">It is not a shelf name with an allowed path.</exception>
        public static ShelfLocation Parse(string text)
        {
            int colon = text.IndexOf(':', StringComparison.Ordinal);
            return colon < 0 ? Of(text, ShelfPath.Root) : Of(text[..colon], text[(colon + 1)..]);
        }

        /// <exception cref="UsageException">The shelf name or the path is not allowed.</exception>
        public static ShelfLocation Of(string shelf, string path)
        {
            if (!ShelfName.TryValidate(shelf, out string? nameError))
            {
                throw new UsageException($"'{shelf}' is not a shelf name: {nameError}");
            }

            if (!ShelfPath.TryNormalize(path.Length == 0 ? ShelfPath.Root : path, out string? normalized, out string? pathError))
            {
                throw new UsageException($"'{path}' is not a path on a shelf: {pathError}");
            }

            return new ShelfLocation(shelf, normalized, normalized == ShelfPath.Root || path.EndsWith(ShelfPath.Separator));
        }

        /// <summary>The path of a file called <paramref name="name"/> here: inside it when it names a directory, else itself.</summary>
        public string FilePathFor(string name)
        {
            return NamesADirectory ? ShelfPath.Combine(Path, name) : Path;
        }

        public override string ToString()
        {
            return $"{Shelf}:{Path}";
        }
    }
}
