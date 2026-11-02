using System.ComponentModel;
using System.Text;
using ByteShelfClient;
using ByteShelfCommon;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ByteShelf.Tools.Mcp
{
    /// <summary>
    /// The MCP tools, one copy for both servers: <c>byteshelf --mcp</c> serves them over stdio, signed in
    /// as the CLI is, and the ByteShelf server at <c>/mcp</c> serves them to a shelf key, offering only
    /// those its <see cref="ToolNeedsAttribute"/> allows. They cover shelves, files, directories and the
    /// trash, answering in the CLI's words. Signing in and users, grants and keys stay on the command
    /// line, since they carry passwords and secrets an agent's transcript should not hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failure becomes an <see cref="McpException"/>, which the SDK hands the model as the tool's
    /// error text, so an agent reads "not signed in" or the server's reason rather than a generic failure.
    /// </para>
    /// <para>
    /// Every tool working on one shelf takes <c>shelf</c> last and optional: left out, it is the one
    /// shelf the caller reaches, which is always so for a shelf key (<see cref="ShelfNameAsync"/>).
    /// </para>
    /// </remarks>
    [McpServerToolType]
    internal sealed class ByteShelfMcpTools
    {
        /// <summary>The largest file download_file answers inline as text, and upload_file takes as text or base64.</summary>
        public const int MaxInlineBytes = 256 * 1024;

        /// <summary>The largest image download_file answers as an image the agent sees.</summary>
        public const int MaxInlineImageBytes = 1024 * 1024;

        /// <summary>The image kinds download_file answers as images, as the descriptions and messages name them; <see cref="ImageTypeOf"/> recognises them.</summary>
        internal const string InlineImageKinds = "PNG, JPEG, GIF or WebP";

        private const string ShelfDescription = "The shelf's name; leave it out when you reach only one shelf, as a shelf key does.";

        /// <summary>Why a tool never reads an encrypted file's contents, after the file's path.</summary>
        internal const string EncryptedReason = "it is encrypted with a password, and passwords never go through tool calls; a person can open it in the web UI, or get it with 'byteshelf get <shelf>:<path> --decrypt'.";

        private static readonly EncryptedFiles NeverDecrypted = EncryptedFiles.LeftOut(EncryptedReason);

        /// <summary>The stored media types that say a file is one of <see cref="InlineImageKinds"/>, so a host without previews reads it up to <see cref="MaxInlineImageBytes"/>.</summary>
        private static readonly HashSet<string> InlineImageTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "image/png", "image/jpeg", "image/gif", "image/webp",
        };

        private readonly IShelfClientSource _clients;
        private readonly ToolHost _host;

        public ByteShelfMcpTools(IShelfClientSource clients, ToolHost host)
        {
            _clients = clients;
            _host = host;
        }

        [McpServerTool(Name = "list_shelves", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("The shelves you can reach, each with its usage and quota. A shelf is one storage place with its own directories, made per purpose.")]
        public Task<string> ListShelvesAsync(CancellationToken cancellationToken)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Shelves(await client.GetShelvesAsync(cancellationToken)));
        }

        [McpServerTool(Name = "shelf_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("One shelf's usage, quota and file count.")]
        public Task<string> ShelfInfoAsync(
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Shelf(await shelfClient.GetInfoAsync(cancellationToken)));
        }

        [McpServerTool(Name = "create_shelf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Admin)]
        [Description("Creates a shelf (admins only). Names are 1 to 63 lowercase letters, digits, '-', '_' and '.'.")]
        public Task<string> CreateShelfAsync(
            [Description("The new shelf's name.")] string shelf,
            [Description("The most bytes it may hold; omit for no limit.")] long? quotaBytes = null,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(cancellationToken, async client => TextFormat.Shelf(await client.CreateShelfAsync(shelf, quotaBytes, cancellationToken)));
        }

        [McpServerTool(Name = "list_directory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("The directories and files in a directory on a shelf.")]
        public Task<string> ListDirectoryAsync(
            [Description("The directory's path, such as /invoices/2026; / for the shelf's root.")] string path = ShelfPath.Root,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Listing(await shelfClient.ListAsync(path, cancellationToken)));
        }

        [McpServerTool(Name = "get_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("A file's record (id, size, content type, SHA-256, times), or a directory's id.")]
        public Task<string> GetInfoAsync(
            [Description("The file's or directory's path.")] string path,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Entry(await Entries.GetAsync(shelfClient, path, cancellationToken)));
        }

        [McpServerTool(Name = "upload_file", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Add)]
        [Description("Stores a file on a shelf, from a local file, from text, or from base64 contents given here; give exactly one. Missing directories are created. A path that is taken is refused unless overwrite is true (which needs write access), and then the old file goes to the trash.")]
        public Task<string> UploadFileAsync(
            [Description("Where to store it, such as /reports/q3.pdf; a path ending in / keeps the local file's name.")] string path,
            [Description("An absolute path to a local file to upload; only the byteshelf CLI's MCP server, on your own machine, can read one.")] string? localPath = null,
            [Description("Text to store, as UTF-8; at most 256 KiB.")] string? text = null,
            [Description("The contents as base64, for a binary file; at most 256 KiB once decoded.")] string? base64 = null,
            [Description("Replace a file already at the path.")] bool overwrite = false,
            [Description("The media type, such as application/pdf; by default from the file name's extension.")] string? contentType = null,
            [Description("What the file is, in up to 2000 characters, for people and for search_files to find it by.")] string? description = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, shelfName) =>
            {
                if (new[] { localPath, text, base64 }.Count(source => source != null) != 1)
                {
                    throw new UsageException("Give one of localPath, text or base64.");
                }

                ShelfLocation location = ShelfLocation.Of(shelfName, path);
                UploadOptions options = new UploadOptions { Overwrite = overwrite, ContentType = contentType, Description = description };
                if (localPath != null)
                {
                    return TextFormat.Stored(await Transfers.UploadFileAsync(shelfClient, Absolute(localPath), location, options, cancellationToken));
                }

                using MemoryStream content = new MemoryStream(text != null ? EncodeText(text) : DecodeBase64(base64!));
                return TextFormat.Stored(await Transfers.UploadStreamAsync(shelfClient, location, content, text != null ? "Text" : "base64", options, cancellationToken));
            });
        }

        // Not marked read-only, though the shelf does not change: a link hands the file to anyone who gets
        // it, so a host that runs read-only tools unasked should still ask before this one.
        [McpServerTool(Name = "share_file", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("Makes a link to one file that anyone who has it can open without signing in, until it runs out. Nothing takes a link back sooner. A file stored over the linked one is a new file the link does not reach; a file in the trash is not found by it until restored.")]
        public Task<string> ShareFileAsync(
            [Description("The file's path.")] string path,
            [Description("How long the link works, in " + LinkLifetimes.Examples + "; 24h by default, at most 7d.")] string? expiresIn = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Link(
                await shelfClient.CreateDownloadLinkAsync(path, LinkLifetimes.ForDownload(expiresIn), cancellationToken)));
        }

        [McpServerTool(Name = "create_upload_link", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Add)]
        [Description("Makes a link that stores one file at a path when anyone who has it PUTs the contents to it, without signing in, until it runs out: for a file too large to pass through this conversation, or one a person or another program will send. Answers the link and a curl command that uses it. The link acts with your access, checked again when it is used. A path that is taken is refused unless overwrite is true (which needs write access), and then the old file goes to the trash.")]
        public Task<string> CreateUploadLinkAsync(
            [Description("Where to store the file, its name included. Missing directories are created.")] string path,
            [Description("Replace a file already at the path.")] bool overwrite = false,
            [Description("The stored file's description, up to 2000 characters.")] string? description = null,
            [Description("How long the link works, in " + LinkLifetimes.Examples + "; 15m by default, at most 24h.")] string? expiresIn = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) =>
            {
                UploadLinkOptions options = new UploadLinkOptions { Overwrite = overwrite, Description = description, Lifetime = LinkLifetimes.ForUpload(expiresIn) };
                return TextFormat.Link(await shelfClient.CreateUploadLinkAsync(path, options, cancellationToken));
            });
        }

        [McpServerTool(Name = "set_description", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Write)]
        [Description("Sets what a file is, in up to 2000 characters of free text, shown with the file and matched by search_files. Omit description to remove it.")]
        public Task<string> SetDescriptionAsync(
            [Description("The file's path.")] string path,
            [Description("The new description; omit or leave blank to remove it.")] string? description = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.File(await shelfClient.SetDescriptionAsync(path, description, cancellationToken)));
        }

        [McpServerTool(Name = "search_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("Finds files on a shelf, under a directory at any depth, that meet every criterion given; none given lists every file. Answers a page of paths by path order, with a cursor when more follow.")]
        public Task<string> SearchFilesAsync(
            [Description("The directory to search under; / (the default) for the whole shelf.")] string under = ShelfPath.Root,
            [Description("Text the file's name or description contains, ignoring case.")] string? text = null,
            [Description("A media type such as application/pdf, or a family such as image/*.")] string? contentType = null,
            [Description("The fewest bytes.")] long? minSizeBytes = null,
            [Description("The most bytes.")] long? maxSizeBytes = null,
            [Description("Stored at or after this ISO 8601 time.")] DateTimeOffset? createdFrom = null,
            [Description("Stored before this ISO 8601 time.")] DateTimeOffset? createdBefore = null,
            [Description("Last changed at or after this ISO 8601 time.")] DateTimeOffset? updatedFrom = null,
            [Description("Last changed before this ISO 8601 time.")] DateTimeOffset? updatedBefore = null,
            [Description("The contents' SHA-256 in hex, to find copies of a file.")] string? sha256 = null,
            [Description("Files per page, 1 to 1000; 100 by default.")] int? limit = null,
            [Description("The cursor a previous page ended with, for the next page.")] string? after = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            FileSearch search = new FileSearch
            {
                Under = under,
                Text = text,
                ContentType = contentType,
                MinSizeBytes = minSizeBytes,
                MaxSizeBytes = maxSizeBytes,
                CreatedFrom = createdFrom,
                CreatedBefore = createdBefore,
                UpdatedFrom = updatedFrom,
                UpdatedBefore = updatedBefore,
                Sha256 = sha256,
                Limit = limit,
                After = after,
            };

            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.SearchResults(
                await shelfClient.SearchAsync(search, cancellationToken),
                MoreAfter));
        }

        [McpServerTool(Name = "download_file", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("Reads a file from a shelf: saved to localPath when given (into it when it is a directory); else answered here, as text when it is UTF-8 of at most 256 KiB, or as an image you can see when it is a " + InlineImageKinds + " of at most 1 MiB. On the server's MCP, any other file stored as an image (image/*) of at most 32 MiB is answered as a JPEG preview at most 1568 pixels on its long side, with its full size and where to get it whole; for any other file there, make a link with share_file instead.")]
        public Task<IEnumerable<ContentBlock>> DownloadFileAsync(
            [Description("The file's path.")] string path,
            [Description("An absolute path to save it to; omit to have it answered here. Only the byteshelf CLI's MCP server, on your own machine, can save one.")] string? localPath = null,
            [Description("Replace a local file already there.")] bool overwrite = false,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) =>
            {
                ShelfFile file = await shelfClient.GetFileAsync(path, cancellationToken);
                if (file.Encrypted)
                {
                    throw new McpException($"{file.Path}: {EncryptedReason}");
                }

                return localPath == null
                    ? await ReadInlineAsync(shelfClient, file, cancellationToken)
                    : new ContentBlock[] { Text(TextFormat.Saved(await Transfers.DownloadToFileAsync(shelfClient, file, Absolute(localPath), overwrite, NeverDecrypted, cancellationToken))) };
            });
        }

        [McpServerTool(Name = "list_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("Everything under a directory on a shelf, at any depth or down to depth levels: one line per directory (ending in /) and file (with its size), by path, a page at a time with a cursor when more follow.")]
        public Task<string> ListTreeAsync(
            [Description("The directory's path; / (the default) for the whole shelf.")] string path = ShelfPath.Root,
            [Description("How many levels down to list: 1 is what the directory holds directly; omit for every level.")] int? depth = null,
            [Description("Entries per page, 1 to 1000; 100 by default.")] int? limit = null,
            [Description("The cursor a previous page ended with, for the page after it.")] string? after = null,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            TreeQuery query = new TreeQuery { Under = path, Depth = depth, Limit = limit, After = after };
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Tree(
                await shelfClient.ListTreeAsync(query, cancellationToken),
                MoreAfter));
        }

        [McpServerTool(Name = "upload_folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Add, LocalFilesOnly = true)]
        [Description("Stores a local folder and everything in it on a shelf, keeping its structure and empty folders; symbolic links are not followed. A file whose path is taken is left out and reported unless overwrite is true, and then the old file goes to the trash.")]
        public Task<string> UploadFolderAsync(
            [Description("The directory to store it as, such as /projects/site; a path ending in / keeps the local folder's name.")] string path,
            [Description("An absolute path to the local folder.")] string localPath,
            [Description("Replace files already at their paths.")] bool overwrite = false,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, shelfName) =>
            {
                ShelfLocation location = ShelfLocation.Of(shelfName, path);
                return FolderAnswer(await FolderTransfers.UploadAsync(shelfClient, Absolute(localPath), location, overwrite, cancellationToken), "Uploaded");
            });
        }

        [McpServerTool(Name = "download_directory", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read, LocalFilesOnly = true)]
        [Description("Saves a directory from a shelf and everything under it as a local folder, keeping its structure and empty directories. A local file already there is left out and reported unless overwrite is true.")]
        public Task<string> DownloadDirectoryAsync(
            [Description("The directory's path; / for the whole shelf.")] string path,
            [Description("An absolute local path: a folder that exists gets the directory inside it under its own name, as download_file does with a file; any other path becomes the folder.")] string localPath,
            [Description("Replace local files already there.")] bool overwrite = false,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, shelfName) =>
            {
                ShelfLocation location = ShelfLocation.Of(shelfName, path);
                return FolderAnswer(await FolderTransfers.DownloadAsync(shelfClient, location, Absolute(localPath), overwrite, cancellationToken, NeverDecrypted), "Saved");
            });
        }

        /// <summary>
        /// What a folder transfer did. One that stopped early is an error, still saying what it did first;
        /// one that left some entries out answers which, since the rest did move.
        /// </summary>
        private static string FolderAnswer(FolderTransferResult result, string verb)
        {
            string answer = TextFormat.FolderTransfer(result, verb);
            return result.StoppedBy == null ? answer : throw new McpException(answer, result.StoppedBy);
        }

        private static string MoreAfter(string cursor)
        {
            return $"More follow: call again with after = {cursor}";
        }

        [McpServerTool(Name = "create_directory", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Add)]
        [Description("Creates a directory on a shelf, with any missing parents; an existing one is answered as it is.")]
        public Task<string> CreateDirectoryAsync(
            [Description("The directory's path.")] string path,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Directory(await shelfClient.CreateDirectoryAsync(path, cancellationToken)));
        }

        [McpServerTool(Name = "move", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Write)]
        [Description("Moves or renames a file or a directory (with everything in it) within its shelf. Never overwrites; missing parent directories are created.")]
        public Task<string> MoveAsync(
            [Description("The file's or directory's current path.")] string path,
            [Description("Its new path.")] string toPath,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Moved(await Entries.MoveAsync(shelfClient, path, toPath, cancellationToken)));
        }

        [McpServerTool(Name = "delete", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Write)]
        [Description("Moves a file, or a directory with everything in it, to the shelf's trash, restorable with restore until the restore window passes.")]
        public Task<string> DeleteAsync(
            [Description("The file's or directory's path.")] string path,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Trashed(await Entries.DeleteAsync(shelfClient, path, cancellationToken)));
        }

        [McpServerTool(Name = "list_trash", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Read)]
        [Description("What a shelf's trash holds: each deletion's id, path, size, and when it will be purged.")]
        public Task<string> ListTrashAsync(
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) => TextFormat.Trash(await shelfClient.GetTrashAsync(cancellationToken)));
        }

        [McpServerTool(Name = "restore", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Write)]
        [Description("Puts back everything one deletion took, where it was. Refused when its name has been taken meanwhile or its directory is itself in the trash.")]
        public Task<string> RestoreAsync(
            [Description("The deletion's id, as list_trash shows it.")] Guid deletionId,
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) =>
            {
                await shelfClient.RestoreAsync(deletionId, cancellationToken);
                return TextFormat.Restored;
            });
        }

        [McpServerTool(Name = "empty_trash", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [ToolNeeds(ToolNeed.Write)]
        [Description("Deletes everything in a shelf's trash for good. It cannot be restored afterwards.")]
        public Task<string> EmptyTrashAsync(
            [Description(ShelfDescription)] string? shelf = null,
            CancellationToken cancellationToken = default)
        {
            return RunOnShelfAsync(shelf, cancellationToken, async (shelfClient, _) =>
            {
                await shelfClient.EmptyTrashAsync(cancellationToken);
                return TextFormat.EmptiedTrash;
            });
        }

        /// <summary>
        /// A file answered in the call: an image the model can see when its bytes are one of
        /// <see cref="InlineImageKinds"/> within <see cref="MaxInlineImageBytes"/>, else its text when it
        /// is UTF-8 within <see cref="MaxInlineBytes"/>, else, for a file stored as an image where the
        /// host makes previews, a preview of it (<see cref="ImagePreview"/>) with where to get the whole
        /// file. The image's type is the one its bytes carry, never the stored one, which is only what
        /// the uploader said: a model refuses an image that does not decode as its stated type. How far
        /// a file is read is <see cref="ReadingOf"/>'s.
        /// </summary>
        /// <exception cref="UsageException">It is larger than that, or neither an image nor text; the message says where to get it instead.</exception>
        private async Task<IEnumerable<ContentBlock>> ReadInlineAsync(IShelfClient shelf, ShelfFile file, CancellationToken cancellationToken)
        {
            string path = file.Path;
            InlineReading reading = ReadingOf(file);
            if (file.SizeBytes > reading.MaxBytes)
            {
                throw NotAnsweredInline($"{path} is {TextFormat.Size(file.SizeBytes)}, more than the {TextFormat.Size(reading.MaxBytes)} answered here{reading.AnsweredAs}");
            }

            // Only a preview can answer an image this large, so the previewer reads it in its turn, and
            // a call waiting for that turn holds none of it.
            if (reading.Previews != null && file.SizeBytes > MaxInlineImageBytes)
            {
                return await PreviewAsync(reading.Previews, file, token => Transfers.ReadAllAsync(shelf, file, token), cancellationToken);
            }

            byte[] content = await Transfers.ReadAllAsync(shelf, file, cancellationToken);
            if (ImageTypeOf(content) is string imageType && content.Length <= MaxInlineImageBytes)
            {
                return new ContentBlock[] { ImageContentBlock.FromBytes(content, imageType), Text($"{path} ({imageType}, {TextFormat.Size(content.Length)})") };
            }

            if (content.Length <= MaxInlineBytes && Utf8TextOf(content) is string text)
            {
                return new ContentBlock[] { Text(text) };
            }

            if (reading.Previews != null)
            {
                return await PreviewAsync(reading.Previews, file, _ => Task.FromResult(content), cancellationToken);
            }

            throw NotAnsweredInline(content.Length > MaxInlineBytes
                ? $"{path} is not a {InlineImageKinds} image, and at {TextFormat.Size(content.Length)} it is more than the {TextFormat.Size(MaxInlineBytes)} answered here as text"
                : $"{path} is neither UTF-8 text nor a {InlineImageKinds} image");
        }

        /// <summary>How far download_file reads a file it answers in the call, what it says it answers it as, and whether a preview may stand in for it.</summary>
        private sealed record InlineReading(long MaxBytes, string AnsweredAs, IImagePreviews? Previews);

        /// <summary>
        /// Where the host makes previews, a file stored as any image is read up to
        /// <see cref="ImagePreview.MaxSourceBytes"/>, to be previewed when it cannot be answered as it is.
        /// Elsewhere only a file stored as one of <see cref="InlineImageKinds"/> is read up to
        /// <see cref="MaxInlineImageBytes"/>; any other file, up to <see cref="MaxInlineBytes"/>.
        /// </summary>
        private InlineReading ReadingOf(ShelfFile file)
        {
            string mediaType = ContentTypes.MediaTypeOf(file.ContentType);
            if (_host.Previews is IImagePreviews previews && mediaType.StartsWith("image/", StringComparison.Ordinal))
            {
                return new InlineReading(ImagePreview.MaxSourceBytes, " as an image or a preview of one", previews);
            }

            return InlineImageTypes.Contains(mediaType)
                ? new InlineReading(MaxInlineImageBytes, " as an image", Previews: null)
                : new InlineReading(MaxInlineBytes, string.Empty, Previews: null);
        }

        /// <summary>A preview of <paramref name="file"/>, with where to get it whole.</summary>
        /// <exception cref="UsageException">No preview could be made; the message says why, and where to get the file.</exception>
        private async Task<IEnumerable<ContentBlock>> PreviewAsync(IImagePreviews previews, ShelfFile file, Func<CancellationToken, Task<byte[]>> readContent, CancellationToken cancellationToken)
        {
            return await previews.MakeAsync(readContent, cancellationToken) switch
            {
                PreviewOutcome.Made made => new ContentBlock[] { ImageContentBlock.FromBytes(made.Preview.Jpeg, "image/jpeg"), Text(PreviewNote(file, made.Preview)) },
                PreviewOutcome.NotMade { Why: NoPreview.TooManyPixels } => throw NotAnsweredInline($"{file.Path} decodes to more pixels than a preview is made from here"),
                PreviewOutcome.NotMade { Why: NoPreview.TooDetailed } => throw NotAnsweredInline($"{file.Path} has too much fine detail for a preview within {TextFormat.Size(MaxInlineImageBytes)}"),
                _ => throw NotAnsweredInline($"{file.Path} ({file.ContentType}) is not an image these tools can make a preview of"),
            };
        }

        /// <summary>What a preview stands in for, and where the whole file is.</summary>
        private string PreviewNote(ShelfFile file, ImagePreview preview)
        {
            return $"{file.Path} is a {preview.OriginalWidth}×{preview.OriginalHeight} {preview.OriginalType} of {TextFormat.Size(file.SizeBytes)}; "
                + $"this is a {preview.Width}×{preview.Height} JPEG preview of it. For the whole file, {WhereToGetIt()}.";
        }

        private static string? Utf8TextOf(byte[] content)
        {
            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        /// <summary>The media type of <paramref name="content"/> when its first bytes are the signature of one of <see cref="InlineImageKinds"/>; else null.</summary>
        internal static string? ImageTypeOf(ReadOnlySpan<byte> content)
        {
            if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                return "image/png";
            }

            if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
            {
                return "image/jpeg";
            }

            if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
            {
                return "image/gif";
            }

            // RIFF, the chunk's four-byte length, then WEBP.
            return content.Length >= 12 && content.StartsWith("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : null;
        }

        /// <summary>A file download_file cannot answer here, saying where to get it: a share link, or a local file where there is one.</summary>
        private UsageException NotAnsweredInline(string reason)
        {
            return new UsageException($"{reason}; {WhereToGetIt()}.");
        }

        /// <summary>How to get a file whole that is not answered here whole.</summary>
        private string WhereToGetIt()
        {
            return _host.ReachesLocalFiles
                ? "save it to a local file with localPath, or make a link with share_file"
                : "make a link to it with share_file and fetch it from there, such as with curl";
        }

        private static TextContentBlock Text(string text)
        {
            return new TextContentBlock { Text = text };
        }

        /// <summary>
        /// <paramref name="text"/> as UTF-8. Capped, as base64 is, at what an agent can sensibly carry in
        /// its own context; anything larger is for a local file or the API.
        /// </summary>
        /// <exception cref="UsageException">It holds more than <see cref="MaxInlineBytes"/>.</exception>
        private byte[] EncodeText(string text)
        {
            return Encoding.UTF8.GetByteCount(text) <= MaxInlineBytes ? Encoding.UTF8.GetBytes(text) : throw TooLarge("text");
        }

        /// <summary>
        /// The bytes <paramref name="base64"/> holds, capped as <see cref="EncodeText"/> is. A string past
        /// twice the cap is refused before decoding: base64 of the cap is 4/3 of it, so twice still
        /// leaves room for line breaks.
        /// </summary>
        /// <exception cref="UsageException">It is not base64, or holds more than <see cref="MaxInlineBytes"/>.</exception>
        private byte[] DecodeBase64(string base64)
        {
            if (base64.Length > 2 * MaxInlineBytes)
            {
                throw TooLarge("base64");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new UsageException("base64 is not valid base64.");
            }

            return bytes.Length <= MaxInlineBytes ? bytes : throw TooLarge("base64");
        }

        private UsageException TooLarge(string parameter)
        {
            string larger = _host.ReachesLocalFiles
                ? "give a larger file as localPath"
                : "store a larger file with the byteshelf CLI or the API";
            return new UsageException($"{parameter} holds more than the {TextFormat.Size(MaxInlineBytes)} a call takes; {larger}.");
        }

        /// <summary>
        /// <paramref name="localPath"/> when it is absolute and these tools run where the agent's files
        /// are. A relative one would be taken from the MCP server's working folder, which is not the
        /// agent's, and on the ByteShelf server no local path is the agent's.
        /// </summary>
        /// <exception cref="UsageException">It is relative, or the tools run on the ByteShelf server.</exception>
        private string Absolute(string localPath)
        {
            if (!_host.ReachesLocalFiles)
            {
                throw new UsageException("This MCP server runs on the ByteShelf server, which cannot reach your files: give the contents as text or base64, or use the byteshelf CLI's MCP server (byteshelf --mcp) on your own machine.");
            }

            return Path.IsPathFullyQualified(localPath)
                ? localPath
                : throw new UsageException($"'{localPath}' is relative; give an absolute local path.");
        }

        /// <summary>
        /// The shelf a call works on: <paramref name="shelf"/> when given and not blank, else the one shelf the caller
        /// reaches. A shelf key reaches exactly one, so an agent with a key never has to name it.
        /// </summary>
        /// <exception cref="UsageException">It is left out and the caller reaches no shelf, or several.</exception>
        private static async Task<string> ShelfNameAsync(IByteShelfClient client, string? shelf, CancellationToken cancellationToken)
        {
            // Blank counts as left out: agents often send "" for an argument they mean to leave out.
            if (!string.IsNullOrWhiteSpace(shelf))
            {
                return shelf;
            }

            IReadOnlyList<ShelfInfo> shelves = await client.GetShelvesAsync(cancellationToken);
            return shelves.Count switch
            {
                1 => shelves[0].Name,
                0 => throw new UsageException("You reach no shelf, so there is none to work on."),
                _ => throw new UsageException($"Name the shelf: you reach {string.Join(", ", shelves.Select(info => info.Name))}."),
            };
        }

        private Task<T> RunOnShelfAsync<T>(string? shelf, CancellationToken cancellationToken, Func<IShelfClient, string, Task<T>> action)
        {
            return RunAsync(cancellationToken, async client =>
            {
                string name = await ShelfNameAsync(client, shelf, cancellationToken);
                return await action(client.Shelf(name), name);
            });
        }

        private async Task<T> RunAsync<T>(CancellationToken cancellationToken, Func<IByteShelfClient, Task<T>> action)
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
