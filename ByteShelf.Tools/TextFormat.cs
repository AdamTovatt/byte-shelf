using System.Globalization;
using System.Text;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>How results read as text, the same in the terminal and in answers to an agent.</summary>
    internal static class TextFormat
    {
        private static readonly string[] SizeUnits = { "B", "KiB", "MiB", "GiB", "TiB" };

        public static string Size(long bytes)
        {
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < SizeUnits.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
                : string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {SizeUnits[unit]}");
        }

        public static string Time(DateTimeOffset instant)
        {
            return instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }

        public static string Shelves(IReadOnlyList<ShelfInfo> shelves)
        {
            return shelves.Count == 0
                ? "No shelves."
                : Lines(shelves.Select(Shelf));
        }

        public static string Shelf(ShelfInfo shelf)
        {
            string quota = shelf.QuotaBytes is long limit ? $" of {Size(limit)}" : string.Empty;
            return $"{shelf.Name}  {Size(shelf.UsedBytes)}{quota} used  {shelf.FileCount} files";
        }

        public static string Listing(DirectoryListing listing)
        {
            StringBuilder text = new StringBuilder();
            text.Append(listing.Directory.Path).Append("  (")
                .Append(listing.Directories.Count).Append(" directories, ")
                .Append(listing.Files.Count).Append(" files)");

            foreach (ShelfDirectory directory in listing.Directories)
            {
                text.AppendLine().Append("  ").Append(directory.Name).Append('/');
            }

            foreach (ShelfFile file in listing.Files)
            {
                text.AppendLine().Append("  ").Append(file.Name).Append("  ").Append(Size(file.SizeBytes)).Append("  ").Append(Time(file.UpdatedAt));
            }

            return text.ToString();
        }

        public static string File(ShelfFile file)
        {
            List<string> lines = new List<string>
            {
                Field("path", file.Path),
                Field("id", file.Id.ToString("D")),
                Field("size", $"{file.SizeBytes} bytes ({Size(file.SizeBytes)})"),
                Field("type", file.ContentType),
                Field("sha256", file.Sha256),
                Field("created", Time(file.CreatedAt)),
                Field("updated", Time(file.UpdatedAt)),
            };

            if (file.Description != null)
            {
                lines.Add(Field("description", file.Description));
            }

            return Lines(lines);
        }

        /// <summary>A labelled line of a record, its value starting in one column; a value's further lines start there too.</summary>
        private static string Field(string label, string value)
        {
            const int ValueColumn = 13;
            string indent = Environment.NewLine + new string(' ', ValueColumn);
            return label.PadRight(ValueColumn) + value.ReplaceLineEndings(indent);
        }

        /// <summary>What a folder transfer did, each file or folder it left out, and what stopped it early.</summary>
        /// <param name="verb">What was done, such as "Uploaded".</param>
        public static string FolderTransfer(FolderTransferResult result, string verb)
        {
            string files = result.Files == 1 ? "1 file" : $"{result.Files} files";
            List<string> lines = new List<string> { $"{verb} {files} ({Size(result.Bytes)}) to {result.Target}." };
            if (result.LeftOut.Count > 0)
            {
                lines.Add($"Left out {CountOf(result.LeftOut.Count, "file or folder", "files or folders")}:");
                lines.AddRange(result.LeftOut.Select(failure => "  " + failure));
            }

            if (result.StoppedBy != null)
            {
                lines.Add($"Stopped before the end: {Failure.From(result.StoppedBy)!.Message}");
            }

            return Lines(lines);
        }

        /// <summary>Why a folder transfer that did not reach everything failed, in one line.</summary>
        public static string FolderTransferFailure(FolderTransferResult result)
        {
            return result.StoppedBy != null
                ? $"Stopped before the end: {Failure.From(result.StoppedBy)!.Message}"
                : $"Left out {CountOf(result.LeftOut.Count, "file or folder", "files or folders")}.";
        }

        private static string CountOf(int count, string one, string many)
        {
            return count == 1 ? $"1 {one}" : $"{count} {many}";
        }

        /// <summary>A directory's tree, one entry per line by path (directories end in /), then <paramref name="more"/> of the cursor when a page follows.</summary>
        public static string Tree(DirectoryTree tree, Func<string, string> more)
        {
            List<string> lines = tree.InOrder()
                .Select(entry => entry.File == null ? entry.Path + "/" : $"{entry.Path}  {Size(entry.File.SizeBytes)}")
                .ToList();

            if (lines.Count == 0)
            {
                return "Nothing under it.";
            }

            return Lines(tree.NextCursor == null ? lines : lines.Append(more(tree.NextCursor)));
        }

        /// <summary>The link alone on its first line, for a script to take, then when it stops working.</summary>
        public static string Link(DownloadLink link)
        {
            return Lines(new[] { link.Url, $"Works for anyone who has it until {Time(link.ExpiresAt)}." });
        }

        /// <summary>One line per file found, then <paramref name="more"/> of the cursor when a page follows.</summary>
        public static string SearchResults(FileSearchResults results, Func<string, string> more)
        {
            if (results.Files.Count == 0)
            {
                return "No files found.";
            }

            IEnumerable<string> lines = results.Files.Select(file => $"{file.Path}  {Size(file.SizeBytes)}  {Time(file.UpdatedAt)}");
            return Lines(results.NextCursor == null ? lines : lines.Append(more(results.NextCursor)));
        }

        public static string Directory(ShelfDirectory directory)
        {
            string path = directory.Path == ShelfPath.Root ? ShelfPath.Root : directory.Path + ShelfPath.Separator;
            return $"{path}  (id {directory.Id:D})";
        }

        public static string Entry(Entry entry)
        {
            return entry.File != null ? File(entry.File) : Directory(entry.Directory!);
        }

        public static string Stored(ShelfFile file)
        {
            return $"Stored {file.Path}  {Size(file.SizeBytes)}  sha256 {file.Sha256}";
        }

        public static string Trash(IReadOnlyList<TrashEntry> entries)
        {
            return entries.Count == 0
                ? "The trash is empty."
                : Lines(entries.Select(TrashEntry));
        }

        public static string TrashEntry(TrashEntry entry)
        {
            string what = entry.Kind == TrashEntryKind.Directory ? $"{entry.Path}/ ({entry.FileCount} files)" : entry.Path;
            return $"{entry.DeletionId:D}  {what}  {Size(entry.SizeBytes)}  deleted {Time(entry.DeletedAt)}, purged {Time(entry.PurgesAt)}";
        }

        public static string Users(IReadOnlyList<UserInfo> users)
        {
            return Lines(users.Select(User));
        }

        public static string User(UserInfo user)
        {
            string roles = user.Roles.Count == 0 ? string.Empty : $"  [{string.Join(", ", user.Roles)}]";
            return $"{user.Id:D}  {user.Email}  {user.DisplayName}{roles}";
        }

        public static string Grants(IReadOnlyList<ShelfAccessGrant> grants)
        {
            return grants.Count == 0
                ? "No one has been given access; admins reach every shelf."
                : Lines(grants.Select(Grant));
        }

        public static string Grant(ShelfAccessGrant grant)
        {
            return $"{grant.Email}  {PermissionNames.Of(grant.Permission)}  ({grant.DisplayName}, {grant.UserId:D})";
        }

        public static string Keys(IReadOnlyList<ShelfKeyInfo> keys)
        {
            return keys.Count == 0
                ? "No keys."
                : Lines(keys.Select(key => $"{key.Id:D}  {key.Name}  {PermissionNames.Of(key.Permission)}  created {Time(key.CreatedAt)}"));
        }

        public static string CreatedKey(CreatedShelfKey created)
        {
            return Lines(new[]
            {
                $"Created key '{created.Key.Name}' ({PermissionNames.Of(created.Key.Permission)}, id {created.Key.Id:D}).",
                "Its secret is shown this once:",
                created.Secret,
            });
        }

        private static string Lines(IEnumerable<string> lines)
        {
            return string.Join(Environment.NewLine, lines);
        }
    }
}
