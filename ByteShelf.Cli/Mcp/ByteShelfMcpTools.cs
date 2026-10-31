using System.ComponentModel;
using System.Text;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Shared;
using ByteShelfClient;
using ByteShelfCommon;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ByteShelf.Cli.Mcp
{
    /// <summary>
    /// <c>byteshelf --mcp</c>'s tools: shelves, files, directories and the trash, signed in as the CLI
    /// is, answering in the CLI's words. Signing in and users, grants and keys stay on the command line,
    /// since they carry passwords and secrets an agent's transcript should not hold.
    /// </summary>
    /// <remarks>
    /// A failure becomes an <see cref="McpException"/>, which the SDK hands the model as the tool's
    /// error text, so an agent reads "not signed in" or the server's reason rather than a generic failure.
    /// </remarks>
    [McpServerToolType]
    internal sealed class ByteShelfMcpTools
    {
        /// <summary>The largest file download_file answers inline.</summary>
        public const int MaxInlineBytes = 256 * 1024;

        private readonly IClientSource _clients;

        public ByteShelfMcpTools(IClientSource clients)
        {
            _clients = clients;
        }

        [McpServerTool(Name = "list_shelves", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("The shelves you can reach, each with its usage and quota. A shelf is one storage place with its own directories, made per purpose.")]
        public Task<string> ListShelvesAsync(CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Shelves(await client.GetShelvesAsync(cancellationToken)));
        }

        [McpServerTool(Name = "shelf_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("One shelf's usage, quota and file count.")]
        public Task<string> ShelfInfoAsync(
            [Description("The shelf's name.")] string shelf,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Shelf(await client.Shelf(shelf).GetInfoAsync(cancellationToken)));
        }

        [McpServerTool(Name = "create_shelf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Creates a shelf (admins only). Names are 1 to 63 lowercase letters, digits, '-', '_' and '.'.")]
        public Task<string> CreateShelfAsync(
            [Description("The new shelf's name.")] string shelf,
            [Description("The most bytes it may hold; omit for no limit.")] long? quotaBytes = null,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Shelf(await client.CreateShelfAsync(shelf, quotaBytes, cancellationToken)));
        }

        [McpServerTool(Name = "list_directory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("The directories and files in a directory on a shelf.")]
        public Task<string> ListDirectoryAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The directory's path, such as /invoices/2026; / for the shelf's root.")] string path = ShelfPath.Root,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Listing(await client.Shelf(shelf).ListAsync(path, cancellationToken)));
        }

        [McpServerTool(Name = "get_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("A file's record (id, size, content type, SHA-256, times), or a directory's id.")]
        public Task<string> GetInfoAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The file's or directory's path.")] string path,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Entry(await Entries.GetAsync(client.Shelf(shelf), path, cancellationToken)));
        }

        [McpServerTool(Name = "upload_file", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Stores a file on a shelf, from a local file or from text given here; give exactly one. Missing directories are created. A path that is taken is refused unless overwrite is true, and then the old file goes to the trash.")]
        public Task<string> UploadFileAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("Where to store it, such as /reports/q3.pdf; a path ending in / keeps the local file's name.")] string path,
            [Description("An absolute path to a local file to upload.")] string? localPath = null,
            [Description("Text to store, as UTF-8.")] string? text = null,
            [Description("Replace a file already at the path.")] bool overwrite = false,
            [Description("The media type, such as application/pdf; by default from the file name's extension.")] string? contentType = null,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(cancellationToken, async client =>
            {
                if ((localPath == null) == (text == null))
                {
                    throw new UsageException("Give either localPath or text, one of them.");
                }

                ShelfLocation location = ShelfLocation.Of(shelf, path);
                IShelfClient shelfClient = client.Shelf(location.Shelf);
                if (localPath != null)
                {
                    return TextFormat.Stored(await Transfers.UploadFileAsync(shelfClient, Absolute(localPath), location, overwrite, contentType, cancellationToken));
                }

                using MemoryStream content = new MemoryStream(Encoding.UTF8.GetBytes(text!));
                return TextFormat.Stored(await Transfers.UploadStreamAsync(shelfClient, location, content, "Text", overwrite, contentType, cancellationToken));
            });
        }

        [McpServerTool(Name = "download_file", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Reads a file from a shelf: saved to localPath when given (into it when it is a directory), else answered here as text when it is UTF-8 and at most 256 KiB.")]
        public Task<string> DownloadFileAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The file's path.")] string path,
            [Description("An absolute path to save it to; omit to have the text answered here.")] string? localPath = null,
            [Description("Replace a local file already there.")] bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(cancellationToken, async client =>
            {
                IShelfClient shelfClient = client.Shelf(shelf);
                return localPath == null
                    ? await Transfers.ReadTextAsync(shelfClient, path, MaxInlineBytes, cancellationToken)
                    : $"Saved {await Transfers.DownloadToFileAsync(shelfClient, path, Absolute(localPath), overwrite, cancellationToken)}";
            });
        }

        [McpServerTool(Name = "create_directory", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Creates a directory on a shelf, with any missing parents; an existing one is answered as it is.")]
        public Task<string> CreateDirectoryAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The directory's path.")] string path,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Directory(await client.Shelf(shelf).CreateDirectoryAsync(path, cancellationToken)));
        }

        [McpServerTool(Name = "move", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Moves or renames a file or a directory (with everything in it) within its shelf. Never overwrites; missing parent directories are created.")]
        public Task<string> MoveAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The file's or directory's current path.")] string path,
            [Description("Its new path.")] string toPath,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => $"Moved to {(await Entries.MoveAsync(client.Shelf(shelf), path, toPath, cancellationToken)).Path}");
        }

        [McpServerTool(Name = "delete", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description("Moves a file, or a directory with everything in it, to the shelf's trash, restorable with restore until the restore window passes.")]
        public Task<string> DeleteAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The file's or directory's path.")] string path,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => $"Moved to the trash: {TextFormat.TrashEntry(await Entries.DeleteAsync(client.Shelf(shelf), path, cancellationToken))}");
        }

        [McpServerTool(Name = "list_trash", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("What a shelf's trash holds: each deletion's id, path, size, and when it will be purged.")]
        public Task<string> ListTrashAsync(
            [Description("The shelf's name.")] string shelf,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Trash(await client.Shelf(shelf).GetTrashAsync(cancellationToken)));
        }

        [McpServerTool(Name = "restore", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Puts back everything one deletion took, where it was. Refused when its name has been taken meanwhile or its directory is itself in the trash.")]
        public Task<string> RestoreAsync(
            [Description("The shelf's name.")] string shelf,
            [Description("The deletion's id, as list_trash shows it.")] Guid deletionId,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client =>
            {
                await client.Shelf(shelf).RestoreAsync(deletionId, cancellationToken);
                return "Restored.";
            });
        }

        [McpServerTool(Name = "empty_trash", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description("Deletes everything in a shelf's trash for good. It cannot be restored afterwards.")]
        public Task<string> EmptyTrashAsync(
            [Description("The shelf's name.")] string shelf,
            CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client =>
            {
                await client.Shelf(shelf).EmptyTrashAsync(cancellationToken);
                return "Emptied the trash.";
            });
        }

        /// <summary>
        /// <paramref name="localPath"/> when it is absolute. A relative one would be taken from the MCP
        /// server's working folder, which is not the agent's.
        /// </summary>
        /// <exception cref="UsageException">It is relative.</exception>
        private static string Absolute(string localPath)
        {
            return Path.IsPathFullyQualified(localPath)
                ? localPath
                : throw new UsageException($"'{localPath}' is relative; give an absolute local path.");
        }

        private async Task<string> RunAsync(CancellationToken cancellationToken, Func<IByteShelfClient, Task<string>> action)
        {
            try
            {
                return await action(await _clients.OpenAsync(cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Failure.From(exception) is Failure failure)
            {
                throw new McpException(failure.Message, exception);
            }
        }
    }
}
