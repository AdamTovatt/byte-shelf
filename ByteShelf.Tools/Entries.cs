using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>A file or a directory, whichever a path named.</summary>
    internal sealed class Entry
    {
        private Entry(ShelfFile? file, ShelfDirectory? directory)
        {
            File = file;
            Directory = directory;
        }

        public ShelfFile? File { get; }

        public ShelfDirectory? Directory { get; }

        public string Path => File?.Path ?? Directory!.Path;

        public static Entry Of(ShelfFile file)
        {
            return new Entry(file, null);
        }

        public static Entry Of(ShelfDirectory directory)
        {
            return new Entry(null, directory);
        }
    }

    /// <summary>
    /// Working on whatever a path names. Commands and tools take a path without saying whether it is
    /// a file or a directory, as a shell's do, and the server's routes differ by kind.
    /// </summary>
    internal static class Entries
    {
        /// <summary>The file or the directory at <paramref name="path"/>.</summary>
        /// <exception cref="ByteShelfException">Neither is there (<see cref="ErrorCodes.NotFound"/>).</exception>
        public static async Task<Entry> GetAsync(IShelfClient shelf, string path, CancellationToken cancellationToken)
        {
            if (path != ShelfPath.Root)
            {
                try
                {
                    return Entry.Of(await shelf.GetFileAsync(path, cancellationToken));
                }
                catch (ByteShelfException notAFile) when (notAFile.Code == ErrorCodes.NotFound)
                {
                    // A directory, or nothing.
                }
            }

            try
            {
                return Entry.Of((await shelf.ListAsync(path, cancellationToken)).Directory);
            }
            catch (ByteShelfException notADirectory) when (notADirectory.Code == ErrorCodes.NotFound)
            {
                throw new ByteShelfException(
                    $"Neither a file nor a directory is at {shelf.Name}:{path}. {notADirectory.Message}",
                    ErrorCodes.NotFound,
                    notADirectory.StatusCode,
                    notADirectory);
            }
        }

        /// <summary>Moves or renames the file or directory at <paramref name="path"/>, answering it where it is now.</summary>
        public static async Task<Entry> MoveAsync(IShelfClient shelf, string path, string toPath, CancellationToken cancellationToken)
        {
            Entry entry = await GetAsync(shelf, path, cancellationToken);
            return entry.File != null
                ? Entry.Of(await shelf.MoveFileAsync(entry.File.Id, toPath, cancellationToken))
                : Entry.Of(await shelf.MoveDirectoryAsync(entry.Directory!.Id, toPath, cancellationToken));
        }

        /// <summary>Moves the file, or the directory with everything in it, at <paramref name="path"/> to the trash.</summary>
        public static async Task<TrashEntry> DeleteAsync(IShelfClient shelf, string path, CancellationToken cancellationToken)
        {
            Entry entry = await GetAsync(shelf, path, cancellationToken);
            return entry.File != null
                ? await shelf.DeleteFileAsync(entry.File.Id, cancellationToken)
                : await shelf.DeleteDirectoryAsync(entry.Directory!.Id, cancellationToken);
        }
    }
}
