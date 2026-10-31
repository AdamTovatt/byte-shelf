using System.Globalization;
using System.Text;
using ByteShelfCommon;

namespace ByteShelf.Cli.Shared
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
            return Lines(new[]
            {
                $"path     {file.Path}",
                $"id       {file.Id:D}",
                $"size     {file.SizeBytes} bytes ({Size(file.SizeBytes)})",
                $"type     {file.ContentType}",
                $"sha256   {file.Sha256}",
                $"created  {Time(file.CreatedAt)}",
                $"updated  {Time(file.UpdatedAt)}",
            });
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
