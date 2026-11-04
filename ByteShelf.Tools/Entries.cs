using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>A file or a directory, whichever a path named.</summary>
    /// <remarks>
    /// Common's TreeEntry and EntryReference have the same two-nullables shape. One closed hierarchy for
    /// all three was left out of 2.0 as a reshaping of working code, not a fix.
    /// </remarks>
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

    /// <summary>What a deletion took at one path, and the encrypted folder its files were in (null for none).</summary>
    internal sealed record DeletedEntry(Entry Entry, EncryptedFolder? EncryptedBy);

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

        /// <summary>Everything under the directory at <paramref name="under"/>, a page of the tree at a time, following each page's cursor.</summary>
        public static IAsyncEnumerable<DirectoryTree> TreePagesAsync(IShelfClient shelf, string under, int pageSize, CancellationToken cancellationToken)
        {
            return PagesAsync(after => shelf.ListTreeAsync(new TreeQuery { Under = under, Limit = pageSize, After = after }, cancellationToken));
        }

        /// <summary>
        /// What deletion <paramref name="deletionId"/> took at <paramref name="under"/> and below it, a page
        /// at a time, following each page's cursor.
        /// </summary>
        public static IAsyncEnumerable<DirectoryTree> DeletionPagesAsync(IShelfClient shelf, Guid deletionId, string under, int pageSize, CancellationToken cancellationToken)
        {
            return PagesAsync(async after => (await shelf.ListDeletionAsync(deletionId, under, after, pageSize, cancellationToken)).Tree);
        }

        /// <summary>
        /// What deletion <paramref name="deletionId"/> took at <paramref name="path"/> (the file or the
        /// directory it deleted, when null), with the encrypted folder its files were in.
        /// </summary>
        /// <exception cref="ByteShelfException">It took nothing there, or is not in the trash (<see cref="ErrorCodes.NotFound"/>).</exception>
        public static async Task<DeletedEntry> GetDeletedAsync(IShelfClient shelf, Guid deletionId, string? path, CancellationToken cancellationToken)
        {
            DeletionTree first = await shelf.ListDeletionAsync(deletionId, path, limit: 1, cancellationToken: cancellationToken);

            // The page starts with the entry at the path; with none, everything the deletion took is under
            // the entry it deleted, which comes first.
            TreeEntry entry = first.Tree.InOrder()[0];
            return new DeletedEntry(entry.File != null ? Entry.Of(entry.File) : Entry.Of(entry.Directory!), first.EncryptedBy);
        }

        /// <summary>Every page <paramref name="list"/> answers, from the first, following each page's cursor.</summary>
        private static async IAsyncEnumerable<DirectoryTree> PagesAsync(Func<string?, Task<DirectoryTree>> list)
        {
            string? after = null;
            do
            {
                DirectoryTree page = await list(after);
                yield return page;
                after = page.NextCursor;
            }
            while (after != null);
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
