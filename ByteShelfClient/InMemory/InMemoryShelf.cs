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
            DirectoryNode current = Root;
            foreach (string name in ShelfPath.GetNames(normalizedPath))
            {
                DirectoryNode? child = LiveChildDirectories(current.Id).FirstOrDefault(directory => directory.Name == name);
                if (child == null)
                {
                    return null;
                }

                current = child;
            }

            return current;
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
            };
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
            public required byte[] Content { get; init; }
            public required string Sha256 { get; init; }
            public required DateTimeOffset CreatedAt { get; init; }
            public required DateTimeOffset UpdatedAt { get; set; }
            public DateTimeOffset? DeletedAt { get; set; }
            public Guid? DeletionId { get; set; }
            public bool IsLive => DeletedAt == null;
        }
    }
}
