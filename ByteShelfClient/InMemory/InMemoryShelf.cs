using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ByteShelfCommon;

namespace ByteShelfClient.InMemory
{
    /// <summary>
    /// One shelf held in memory: its directory tree, files with their bytes, grants and keys. Files and
    /// directories share one namespace per directory, and deleted ones stay until the purge, as on the
    /// server. Every member runs under <see cref="InMemoryStore.Gate"/>.
    /// </summary>
    internal sealed class InMemoryShelf
    {
        public InMemoryShelf(string name, long? quotaBytes, DateTimeOffset createdAt)
        {
            Id = Guid.NewGuid();
            Name = name;
            QuotaBytes = quotaBytes;
            CreatedAt = createdAt;
            Root = new DirectoryNode { Id = Guid.NewGuid(), ParentId = null, Name = string.Empty, CreatedAt = createdAt };
            Directories.Add(Root);
        }

        public Guid Id { get; }

        public string Name { get; set; }

        public long? QuotaBytes { get; set; }

        public DateTimeOffset CreatedAt { get; }

        public DirectoryNode Root { get; }

        public List<DirectoryNode> Directories { get; } = new List<DirectoryNode>();

        public List<FileNode> Files { get; } = new List<FileNode>();

        public List<ShelfAccessGrant> Grants { get; } = new List<ShelfAccessGrant>();

        public List<ShelfKeyInfo> Keys { get; } = new List<ShelfKeyInfo>();

        /// <summary>Every byte stored, the trash included, which is what the quota counts.</summary>
        public long UsedBytes => Files.Sum(file => file.Content.LongLength);

        public ShelfInfo ToInfo()
        {
            return new ShelfInfo
            {
                Id = Id,
                Name = Name,
                QuotaBytes = QuotaBytes,
                UsedBytes = UsedBytes,
                ReservedBytes = 0,
                FileCount = Files.Count(file => file.IsLive),
                CreatedAt = CreatedAt,
            };
        }

        public IEnumerable<DirectoryNode> LiveChildDirectories(Guid directoryId)
        {
            return Directories.Where(directory => directory.ParentId == directoryId && directory.IsLive);
        }

        public IEnumerable<FileNode> LiveFilesIn(Guid directoryId)
        {
            return Files.Where(file => file.DirectoryId == directoryId && file.IsLive);
        }

        /// <summary>The live directory at <paramref name="normalizedPath"/>, or null when any part of it is missing.</summary>
        public DirectoryNode? FindDirectory(string normalizedPath)
        {
            (DirectoryNode deepest, bool reached) = Walk(normalizedPath);
            return reached ? deepest : null;
        }

        /// <summary>Walks down from the root by name: the deepest live directory on the way, and whether it is the path's own.</summary>
        private (DirectoryNode Deepest, bool Reached) Walk(string normalizedPath)
        {
            DirectoryNode current = Root;
            foreach (string name in ShelfPath.GetNames(normalizedPath))
            {
                DirectoryNode? child = LiveChildDirectories(current.Id).FirstOrDefault(directory => directory.Name == name);
                if (child == null)
                {
                    return (current, false);
                }

                current = child;
            }

            return (current, true);
        }

        public DirectoryNode GetDirectory(EntryReference reference)
        {
            DirectoryNode? directory = reference.Id != null
                ? Directories.FirstOrDefault(candidate => candidate.Id == reference.Id && candidate.IsLive)
                : FindDirectory(reference.Path!);

            return directory ?? throw InMemoryErrors.NotFound($"There is no directory '{reference}' on this shelf.");
        }

        public FileNode GetFile(EntryReference reference)
        {
            FileNode? file = null;
            if (reference.Id != null)
            {
                file = Files.FirstOrDefault(candidate => candidate.Id == reference.Id && candidate.IsLive);
            }
            else if (reference.Path != ShelfPath.Root)
            {
                DirectoryNode? directory = FindDirectory(ShelfPath.GetParent(reference.Path!));
                file = directory == null ? null : LiveFilesIn(directory.Id).FirstOrDefault(candidate => candidate.Name == ShelfPath.GetName(reference.Path!));
            }

            return file ?? throw InMemoryErrors.NotFound($"There is no file '{reference}' on this shelf.");
        }

        /// <summary>The live directory at <paramref name="normalizedPath"/>, creating every missing one on the way.</summary>
        public DirectoryNode EnsureDirectory(string normalizedPath, DateTimeOffset at)
        {
            DirectoryNode current = Root;
            string currentPath = ShelfPath.Root;
            foreach (string name in ShelfPath.GetNames(normalizedPath))
            {
                currentPath = ShelfPath.Combine(currentPath, name);
                DirectoryNode? child = LiveChildDirectories(current.Id).FirstOrDefault(directory => directory.Name == name);
                if (child == null)
                {
                    if (LiveFilesIn(current.Id).Any(file => file.Name == name))
                    {
                        throw InMemoryErrors.Conflict($"'{currentPath}' is a file, not a directory.");
                    }

                    child = new DirectoryNode { Id = Guid.NewGuid(), ParentId = current.Id, Name = name, CreatedAt = at };
                    Directories.Add(child);
                }

                current = child;
            }

            return current;
        }

        /// <summary>Throws unless no live directory, nor a live file unless <paramref name="mayReplaceFile"/>, has the name.</summary>
        public void EnsureNameAvailable(Guid directoryId, string name, string pathForMessage, bool mayReplaceFile)
        {
            if (LiveChildDirectories(directoryId).Any(directory => directory.Name == name))
            {
                throw InMemoryErrors.Conflict($"'{pathForMessage}' is a directory.");
            }

            if (!mayReplaceFile && LiveFilesIn(directoryId).Any(file => file.Name == name))
            {
                throw InMemoryErrors.Conflict($"A file already exists at '{pathForMessage}'.");
            }
        }

        /// <summary>Readies <paramref name="normalizedPath"/> to receive a moved entry; answers its directory and name.</summary>
        public (DirectoryNode Parent, string Name) PrepareDestination(string normalizedPath, DateTimeOffset at)
        {
            if (normalizedPath == ShelfPath.Root)
            {
                throw InMemoryErrors.Invalid("Nothing can be moved to the root itself; give it a name.");
            }

            DirectoryNode parent = EnsureDirectory(ShelfPath.GetParent(normalizedPath), at);
            string name = ShelfPath.GetName(normalizedPath);
            EnsureNameAvailable(parent.Id, name, normalizedPath, mayReplaceFile: false);
            return (parent, name);
        }

        /// <summary>The full path of a directory, live or not.</summary>
        public string PathOf(DirectoryNode directory)
        {
            List<string> names = new List<string>();
            for (DirectoryNode? current = directory; current?.ParentId != null; current = Directories.FirstOrDefault(parent => parent.Id == current.ParentId))
            {
                names.Insert(0, current.Name);
            }

            return ShelfPath.Root + string.Join(ShelfPath.Separator, names);
        }

        public string PathOf(Guid directoryId)
        {
            return PathOf(Directories.First(directory => directory.Id == directoryId));
        }

        /// <summary>The directory itself when it is encrypted, else its nearest encrypted ancestor; null for none.</summary>
        public DirectoryNode? EncryptedFolderOf(Guid directoryId)
        {
            for (DirectoryNode? current = Directories.First(directory => directory.Id == directoryId); current != null; current = Directories.FirstOrDefault(parent => parent.Id == current.ParentId))
            {
                if (current.EncryptionCheck != null)
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>The encrypted directory a file in <paramref name="normalizedDirectoryPath"/> is, or would be, under.</summary>
        public DirectoryNode? EncryptedFolderFor(string normalizedDirectoryPath)
        {
            return EncryptedFolderOf(Walk(normalizedDirectoryPath).Deepest.Id);
        }

        public EncryptedFolder? ToEncryptedFolder(DirectoryNode? folder)
        {
            return folder?.EncryptionCheck is byte[] check
                ? new EncryptedFolder { Directory = ToShelfDirectory(folder), Check = Convert.ToBase64String(check) }
                : null;
        }

        /// <summary>As the server reserves an upload's room: throws unless <paramref name="addedBytes"/> more fit under the quota.</summary>
        public void RequireRoomFor(long addedBytes)
        {
            if (QuotaBytes is long quota && addedBytes > quota - UsedBytes)
            {
                throw new ByteShelfException(
                    $"The shelf '{Name}' has {Math.Max(quota - UsedBytes, 0)} bytes free of its {quota}, too few for {addedBytes} more.",
                    ErrorCodes.QuotaExceeded);
            }
        }

        /// <summary>
        /// As the server: bytes sent as <paramref name="encrypted"/> must at least be an age file, the one
        /// thing it can tell without the password. <paramref name="path"/> is a new file's, null for a swap.
        /// </summary>
        public static void RequireAgeFile(bool encrypted, byte[] bytes, string? path)
        {
            if (encrypted && !FileEncryption.StartsAsAgeFile(bytes))
            {
                throw InMemoryErrors.Invalid(path != null
                    ? $"'{path}' was sent as encrypted, but it is not an age file."
                    : "The new contents were sent as encrypted, but they are not an age file.");
            }
        }

        // The encryption rules below are the server's ShelfEncryption's, written again over this client's
        // nodes rather than the server's rows, with the same messages; the client contract tests run
        // against both hold them together. One home would mean an abstraction over both trees.

        /// <summary>As the server's ShelfEncryption.RequireStoreAsync: never a plain file under an encrypted directory.</summary>
        public void RequireStore(string normalizedPath, bool encrypted)
        {
            if (!encrypted && EncryptedFolderFor(ShelfPath.GetParent(normalizedPath)) is DirectoryNode folder)
            {
                throw InMemoryErrors.Conflict(
                    $"'{normalizedPath}' is under the encrypted folder '{PathOf(folder)}': store it encrypted with that folder's password, as the web UI and the byteshelf CLI do.");
            }
        }

        /// <summary>
        /// As the server's ShelfEncryption.RequireSwapAsync: a swap only encrypts a plain file or decrypts
        /// an encrypted one, and never leaves a plain file under an encrypted directory.
        /// </summary>
        public void RequireSwap(FileNode file, string path, bool encrypted)
        {
            if (file.Encrypted == encrypted)
            {
                throw InMemoryErrors.Conflict(encrypted
                    ? $"'{path}' is already encrypted: swapping its contents in place only encrypts a plain file or decrypts an encrypted one."
                    : $"'{path}' is not encrypted: swapping its contents in place only encrypts a plain file or decrypts an encrypted one.");
            }

            RequireStore(path, encrypted);
        }

        /// <summary>As the server's ShelfEncryption.RequireMoveAsync: into an encrypted directory only from inside it.</summary>
        public void RequireMove(Guid? movedDirectoryId, Guid currentParentId, Guid destinationParentId, string pathForMessage)
        {
            DirectoryNode? destination = EncryptedFolderOf(destinationParentId);
            if (destination != null && EncryptedFolderOf(movedDirectoryId ?? currentParentId)?.Id != destination.Id)
            {
                throw InMemoryErrors.Conflict(
                    $"'{pathForMessage}' cannot move into the encrypted folder '{PathOf(destination)}' from outside it, since it is not encrypted with that folder's password. Store it there anew instead.");
            }
        }

        /// <summary>As the server's ShelfEncryption.RequireLinkTargetAsync: a link sends a plain file.</summary>
        public void RequireLinkTarget(string normalizedPath)
        {
            string parentPath = ShelfPath.GetParent(normalizedPath);
            if (EncryptedFolderFor(parentPath) is DirectoryNode folder)
            {
                throw InMemoryErrors.Conflict($"An upload link sends a plain file, and '{normalizedPath}' is under the encrypted folder '{PathOf(folder)}'.");
            }

            if (FindDirectory(parentPath) is DirectoryNode parent
                && LiveFilesIn(parent.Id).Any(file => file.Name == ShelfPath.GetName(normalizedPath) && file.Encrypted))
            {
                throw InMemoryErrors.Conflict($"'{normalizedPath}' is encrypted, and an upload link would replace it with a plain file.");
            }
        }

        /// <summary>Every directory under <paramref name="directory"/>, live or in the trash, deepest last.</summary>
        public IEnumerable<DirectoryNode> Descendants(DirectoryNode directory)
        {
            foreach (DirectoryNode child in Directories.Where(candidate => candidate.ParentId == directory.Id).ToList())
            {
                yield return child;
                foreach (DirectoryNode descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// <paramref name="directory"/> and every live directory reached from it through live ones, down
        /// to <paramref name="levels"/> levels below it when set (0: the directory alone).
        /// </summary>
        public IEnumerable<DirectoryNode> LiveSubtree(DirectoryNode directory, int? levels = null)
        {
            yield return directory;
            if (levels <= 0)
            {
                yield break;
            }

            foreach (DirectoryNode child in LiveChildDirectories(directory.Id).ToList())
            {
                foreach (DirectoryNode descendant in LiveSubtree(child, levels - 1))
                {
                    yield return descendant;
                }
            }
        }

        public ShelfFile ToShelfFile(FileNode file)
        {
            return new ShelfFile
            {
                Id = file.Id,
                ShelfId = Id,
                DirectoryId = file.DirectoryId,
                Path = ShelfPath.Combine(PathOf(file.DirectoryId), file.Name),
                Name = file.Name,
                ContentType = file.ContentType,
                SizeBytes = file.Content.LongLength,
                Sha256 = file.Sha256,
                CreatedAt = file.CreatedAt,
                UpdatedAt = file.UpdatedAt,
                Description = file.Description,
                Encrypted = file.Encrypted,
            };
        }

        public ShelfDirectory ToShelfDirectory(DirectoryNode directory)
        {
            return new ShelfDirectory
            {
                Id = directory.Id,
                ShelfId = Id,
                ParentId = directory.ParentId,
                Path = PathOf(directory),
                Name = directory.Name,
                CreatedAt = directory.CreatedAt,
                Encrypted = directory.EncryptionCheck != null,
            };
        }

        /// <summary>The directory as its parent's listing shows it, with the live files under it at any depth.</summary>
        public ListedDirectory ToListedDirectory(DirectoryNode directory)
        {
            List<FileNode> files = LiveSubtree(directory).SelectMany(node => LiveFilesIn(node.Id)).ToList();
            return new ListedDirectory
            {
                Directory = ToShelfDirectory(directory),
                SizeBytes = files.Sum(file => file.Content.LongLength),
                FileCount = files.Count,
                EncryptedFileCount = files.Count(file => file.Encrypted && !InEncryptedBelow(directory, file)),
            };
        }

        /// <summary>Whether <paramref name="file"/> is under an encrypted directory below <paramref name="top"/>, whose own count it is in.</summary>
        private bool InEncryptedBelow(DirectoryNode top, FileNode file)
        {
            DirectoryNode? encrypted = EncryptedFolderOf(file.DirectoryId);
            return encrypted != null && encrypted.Id != top.Id && PathOf(encrypted).StartsWith(PathOf(top) + ShelfPath.Separator, StringComparison.Ordinal);
        }

        /// <summary>The lowercase-hex SHA-256 of <paramref name="content"/>, as the server stores it.</summary>
        public static string Sha256Of(byte[] content)
        {
            StringBuilder hex = new StringBuilder(64);
            foreach (byte value in SHA256.HashData(content))
            {
                hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        internal sealed class DirectoryNode
        {
            public required Guid Id { get; init; }
            public required Guid? ParentId { get; set; }
            public required string Name { get; set; }
            public required DateTimeOffset CreatedAt { get; init; }
            public byte[]? EncryptionCheck { get; set; }
            public DateTimeOffset? DeletedAt { get; set; }
            public Guid? DeletionId { get; set; }
            public bool IsLive => DeletedAt == null;
        }

        internal sealed class FileNode
        {
            public required Guid Id { get; init; }
            public required Guid DirectoryId { get; set; }
            public required string Name { get; set; }
            public required string ContentType { get; init; }
            public required byte[] Content { get; set; }
            public required string Sha256 { get; set; }
            public required DateTimeOffset CreatedAt { get; init; }
            public required DateTimeOffset UpdatedAt { get; set; }
            public string? Description { get; set; }
            public bool Encrypted { get; set; }
            public DateTimeOffset? DeletedAt { get; set; }
            public Guid? DeletionId { get; set; }
            public bool IsLive => DeletedAt == null;
        }
    }
}
