using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>What a folder transfer did: the files it moved, what it left out and why, and what stopped it early, if anything did.</summary>
    internal sealed class FolderTransferResult
    {
        private readonly List<string> _leftOut = new List<string>();

        public FolderTransferResult(string target)
        {
            Target = target;
        }

        /// <summary>Where the files went: a directory on the shelf or a local folder.</summary>
        public string Target { get; }

        public int Files { get; private set; }

        public long Bytes { get; private set; }

        /// <summary>Each file or folder left out, as "what: why"; a folder left out takes everything in it along.</summary>
        public IReadOnlyList<string> LeftOut => _leftOut;

        /// <summary>The failure that ended the transfer before it reached everything; null when it went to the end.</summary>
        public Exception? StoppedBy { get; private set; }

        /// <summary>Whether everything was moved.</summary>
        public bool IsComplete => _leftOut.Count == 0 && StoppedBy == null;

        public void Add(long bytes)
        {
            Files++;
            Bytes += bytes;
        }

        public void LeaveOut(string what, string why)
        {
            _leftOut.Add($"{what}: {why}");
        }

        public void Stop(Exception exception)
        {
            StoppedBy = exception;
        }
    }

    /// <summary>
    /// A whole local folder to a shelf, and a whole directory from one, keeping the structure, empty
    /// folders included, and placed as <see cref="Transfers"/> places one file. A file or folder that
    /// cannot be moved is left out and reported, and the rest go on; a failure that would stop every
    /// file alike (signed out, no permission, the quota, a read-only replica, the server out of reach)
    /// stops the transfer, and the result still says what was done before it.
    /// </summary>
    internal static class FolderTransfers
    {
        /// <summary>
        /// Stores the local folder <paramref name="localFolder"/> and everything in it at
        /// <paramref name="location"/>, under the folder's own name when that names a directory.
        /// </summary>
        /// <param name="encryptor">Encrypts every file, as a folder under an encrypted directory must be; null stores them as they are.</param>
        /// <exception cref="UsageException">There is no such local folder, or it is a symbolic link.</exception>
        public static async Task<FolderTransferResult> UploadAsync(IShelfClient shelf, string localFolder, ShelfLocation location, bool overwrite, CancellationToken cancellationToken, Encryptor? encryptor = null)
        {
            DirectoryInfo folder = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(localFolder)));
            if (!folder.Exists)
            {
                throw new UsageException($"There is no folder {folder.FullName}.");
            }

            if (folder.LinkTarget != null)
            {
                throw new UsageException($"{folder.FullName} is a symbolic link, which is never followed; give the folder it points at.");
            }

            string path = location.FilePathFor(folder.Name);
            FolderTransferResult result = new FolderTransferResult($"{location.Shelf}:{path}");
            await StopOnAsync(result, cancellationToken, async () =>
            {
                await shelf.CreateDirectoryAsync(path, cancellationToken);
                await UploadFolderAsync(shelf, folder, path, overwrite, encryptor, result, cancellationToken);
            });

            return result;
        }

        /// <summary>
        /// Saves the directory at <paramref name="location"/> (the whole shelf for its root) and everything
        /// under it as a local folder: <paramref name="localTarget"/>, or inside it under the directory's
        /// name when it is a folder that exists, or that name in the working folder when null.
        /// </summary>
        /// <param name="encrypted">What to do with encrypted files: decrypt them, save them as age files, or leave them out.</param>
        /// <param name="pageSize">How many entries to ask for at a time; tests make it small to page over few files.</param>
        public static async Task<FolderTransferResult> DownloadAsync(
            IShelfClient shelf,
            ShelfLocation location,
            string? localTarget,
            bool overwrite,
            EncryptedFiles encrypted,
            CancellationToken cancellationToken,
            int pageSize = TreeQuery.MaxLimit)
        {
            string name = location.Path == ShelfPath.Root ? location.Shelf : ShelfPath.GetName(location.Path);
            return await SaveTreeAsync(
                name, location.Path, Entries.TreePagesAsync(shelf, location.Path, pageSize, cancellationToken), FileSource.Live(shelf), localTarget, overwrite, encrypted, cancellationToken);
        }

        /// <summary>
        /// Saves a directory a deletion took, without restoring it, as a local folder named as the
        /// directory was, placed as <see cref="DownloadAsync"/> places one, keeping the paths it had.
        /// </summary>
        /// <param name="deletionId">A deletion that took the directory, or one above it.</param>
        /// <param name="directoryPath">The path the deleted directory had.</param>
        public static async Task<FolderTransferResult> DownloadDeletionAsync(
            IShelfClient shelf,
            Guid deletionId,
            string directoryPath,
            string? localTarget,
            bool overwrite,
            EncryptedFiles encrypted,
            CancellationToken cancellationToken,
            int pageSize = TreeQuery.MaxLimit)
        {
            return await SaveTreeAsync(
                ShelfPath.GetName(directoryPath),
                directoryPath,
                Entries.DeletionPagesAsync(shelf, deletionId, directoryPath, pageSize, cancellationToken),
                FileSource.Deleted(shelf, deletionId),
                localTarget,
                overwrite,
                encrypted,
                cancellationToken);
        }

        /// <summary>
        /// Saves the entries <paramref name="pages"/> hold under <paramref name="under"/> as a local folder
        /// named <paramref name="name"/>, placed as <see cref="DownloadAsync"/> says; an entry at
        /// <paramref name="under"/> itself is that folder.
        /// </summary>
        private static async Task<FolderTransferResult> SaveTreeAsync(
            string name,
            string under,
            IAsyncEnumerable<DirectoryTree> pages,
            FileSource source,
            string? localTarget,
            bool overwrite,
            EncryptedFiles encrypted,
            CancellationToken cancellationToken)
        {
            string root = Path.GetFullPath(localTarget == null ? name : Directory.Exists(localTarget) ? Path.Combine(localTarget, name) : localTarget);
            FolderTransferResult result = new FolderTransferResult(root);
            LocalNames names = new LocalNames(root, under);

            await StopOnAsync(result, cancellationToken, async () =>
            {
                Directory.CreateDirectory(root);
                await foreach (DirectoryTree page in pages)
                {
                    foreach (TreeEntry entry in page.InOrder().Where(entry => entry.Path != under))
                    {
                        await RunAsync(entry.Path, result, () => entry.File == null
                            ? SaveDirectoryAsync(entry.Path, names)
                            : SaveFileAsync(source, entry.File, names, overwrite, encrypted, result, cancellationToken));
                    }
                }
            });

            return result;
        }

        private static Task SaveDirectoryAsync(string path, LocalNames names)
        {
            Directory.CreateDirectory(names.Claim(path, isDirectory: true));
            return Task.CompletedTask;
        }

        private static async Task SaveFileAsync(FileSource source, ShelfFile file, LocalNames names, bool overwrite, EncryptedFiles encrypted, FolderTransferResult result, CancellationToken cancellationToken)
        {
            string target = names.Claim(encrypted.NameFor(file, file.Path), isDirectory: false);
            if (Directory.Exists(target))
            {
                throw new UsageException("a local folder of that name is in the way.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await Transfers.DownloadToFileAsync(source, file, target, overwrite, encrypted, cancellationToken);
            result.Add(file.SizeBytes);
        }

        private static async Task UploadFolderAsync(IShelfClient shelf, DirectoryInfo folder, string shelfPath, bool overwrite, Encryptor? encryptor, FolderTransferResult result, CancellationToken cancellationToken)
        {
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                await RunAsync(entry.FullName, result, async () =>
                {
                    if (entry.LinkTarget != null)
                    {
                        throw new UsageException("a symbolic link, which is never followed.");
                    }

                    string path = ShelfPath.Combine(shelfPath, entry.Name);
                    if (entry is DirectoryInfo subfolder)
                    {
                        await shelf.CreateDirectoryAsync(path, cancellationToken);
                        await UploadFolderAsync(shelf, subfolder, path, overwrite, encryptor, result, cancellationToken);
                        return;
                    }

                    ShelfFile stored = await Transfers.UploadFileAsync(shelf, entry.FullName, path, new UploadOptions { Overwrite = overwrite }, cancellationToken, encryptor);
                    result.Add(stored.SizeBytes);
                });
            }
        }

        /// <summary>Runs the transfer, keeping a failure that stops it in the result rather than losing what was done.</summary>
        /// <remarks>Internal, with <see cref="RunAsync"/>, for <see cref="Encrypting"/>, whose runs report as transfers do.</remarks>
        internal static async Task StopOnAsync(FolderTransferResult result, CancellationToken cancellationToken, Func<Task> transfer)
        {
            try
            {
                await transfer();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Failure.From(exception) != null)
            {
                result.Stop(exception);
            }
        }

        /// <summary>Runs one entry's step; a failure of that entry alone is reported and the transfer goes on.</summary>
        internal static async Task RunAsync(string what, FolderTransferResult result, Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (Exception exception) when (StopsOnlyThisEntry(exception))
            {
                result.LeaveOut(what, Failure.From(exception)!.Message);
            }
        }

        private static bool StopsOnlyThisEntry(Exception exception)
        {
            return exception switch
            {
                ByteShelfException refused => refused.Code is not (ErrorCodes.Unauthenticated or ErrorCodes.PermissionDenied or ErrorCodes.QuotaExceeded or ErrorCodes.ReadOnlyReplica),
                UsageException or ArgumentException or IOException or UnauthorizedAccessException or AgeException => true,
                _ => false,
            };
        }

        /// <summary>
        /// The local paths a download's entries take. Names that differ only in case on the shelf are one
        /// name on a disk that ignores case (as macOS and Windows disks do by default), so the second of
        /// them is left out, with what under it, rather than saved over the first or merged into it. A
        /// local folder on the way that is a symbolic link is never followed, as on upload, so nothing
        /// lands outside the target through one.
        /// </summary>
        /// <remarks>
        /// The tests run on a Linux disk, which keeps case, so nothing there reaches the case refusals,
        /// and no test covers them.
        /// </remarks>
        private sealed class LocalNames
        {
            private readonly string _root;
            private readonly string _shelfPath;
            private readonly Dictionary<string, string> _claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _leftOutDirectories = new List<string>();

            public LocalNames(string root, string shelfPath)
            {
                _root = root;
                _shelfPath = shelfPath;
            }

            /// <summary>The local path for the entry at <paramref name="path"/>, claimed for it.</summary>
            /// <exception cref="UsageException">It cannot have one of its own.</exception>
            public string Claim(string path, bool isDirectory)
            {
                string? leftOutAbove = _leftOutDirectories.FirstOrDefault(directory => path.StartsWith(directory + ShelfPath.Separator, StringComparison.Ordinal));
                if (leftOutAbove != null)
                {
                    throw new UsageException($"it is inside {leftOutAbove}, which was left out.");
                }

                string local = LocalPathFor(path);
                string? link = LinkOnTheWay(isDirectory ? local : Path.GetDirectoryName(local)!);
                if (link != null)
                {
                    if (isDirectory)
                    {
                        _leftOutDirectories.Add(path);
                    }

                    throw new UsageException($"{link} is a symbolic link, which is never followed.");
                }

                bool taken = isDirectory ? Directory.Exists(local) : File.Exists(local);
                if (_claimed.TryGetValue(local, out string? other) && taken)
                {
                    if (isDirectory)
                    {
                        _leftOutDirectories.Add(path);
                    }

                    throw new UsageException($"this disk ignores case, so it would land on {other}.");
                }

                _claimed[local] = path;
                return local;
            }

            /// <summary>The first folder from <paramref name="folder"/> up to the root (not the root itself) that is a symbolic link, or null.</summary>
            private string? LinkOnTheWay(string folder)
            {
                string root = Path.TrimEndingDirectorySeparator(_root);
                for (string? current = folder; current != null && current.Length > root.Length; current = Path.GetDirectoryName(current))
                {
                    if (new DirectoryInfo(current).LinkTarget != null)
                    {
                        return current;
                    }
                }

                return null;
            }

            private string LocalPathFor(string path)
            {
                string relative = _shelfPath == ShelfPath.Root ? path[1..] : path[(_shelfPath.Length + 1)..];
                string local = Path.GetFullPath(Path.Combine(_root, Path.Combine(relative.Split(ShelfPath.Separator))));
                if (!local.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new UsageException("its name is not one a local file can have here.");
                }

                return local;
            }
        }
    }
}
