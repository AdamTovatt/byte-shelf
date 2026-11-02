using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using ByteShelf.Server.Backup.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Backup.Services
{
    /// <summary>
    /// Writes a consistent zip of a root while its server runs: a snapshot of the database
    /// (<c>VACUUM INTO</c>, which reads one transaction's view), then the objects the snapshot's files
    /// point at, then the keys. Unzipped, it is a root a server starts from.
    /// </summary>
    /// <remarks>
    /// The server may purge a file after the snapshot and remove its object before it is copied: such a
    /// file is purged from the snapshot too, logged as the server logged it. A file whose contents were
    /// swapped meanwhile (encrypted or decrypted in place) is backed up with its new contents. Uploads in progress are
    /// dropped, since their staged bytes are not copied. Every object is checked against its SHA-256 on
    /// the way into the zip, and an object missing while the server still points at it or not matching
    /// its hash fails the backup rather than carrying the damage on.
    /// </remarks>
    public static class RootBackup
    {
        /// <summary>
        /// Writes the backup of <paramref name="root"/> to <paramref name="zipPath"/>, outside the root,
        /// replacing a file there only once the whole zip is written.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="zipPath"/> is inside the root.</exception>
        /// <exception cref="InvalidDataException">An object is missing or does not match its SHA-256.</exception>
        public static Task<BackupSummary> WriteAsync(RootFolder root, string zipPath, CancellationToken cancellationToken)
        {
            return WriteAsync(root, zipPath, () => Task.CompletedTask, cancellationToken);
        }

        /// <param name="afterSnapshot">Runs between the snapshot and copying the objects, for tests to change the root there.</param>
        internal static async Task<BackupSummary> WriteAsync(RootFolder root, string zipPath, Func<Task> afterSnapshot, CancellationToken cancellationToken)
        {
            string target = Path.GetFullPath(zipPath);
            if (IsInside(target, root.Path))
            {
                throw new ArgumentException($"Write the backup outside the root ({root.Path}).", nameof(zipPath));
            }

            string partialZip = target + ".partial";
            string snapshotPath = target + ".db.partial";

            // Held for the whole run, so a second backup to the same path fails instead of sharing its files.
            await using FileStream backupLock = LockBackupPath(target);
            DeleteIfPresent(partialZip);
            DeleteIfPresent(snapshotPath);

            try
            {
                await SnapshotAsync(root, snapshotPath);
                await afterSnapshot();

                BackupSummary summary;
                await using (FileStream file = new FileStream(partialZip, OwnerOnly.Write(FileMode.CreateNew)))
                using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    await using (StorageDatabase live = new StorageDatabase(root.ConnectionString))
                    await using (StorageDatabase snapshot = new StorageDatabase(RootFolder.ConnectionStringFor(snapshotPath, pooling: false)))
                    {
                        CopiedObjects copied = await AddObjectsAsync(zip, root, snapshot, live, cancellationToken);
                        IReadOnlyList<SwappedFile> swapped = await AddSwappedObjectsAsync(zip, root, snapshot, live, copied, cancellationToken);
                        int filesPurged = await PruneSnapshotAsync(snapshot, swapped, copied.Missing);
                        summary = new BackupSummary { ObjectCount = copied.Added.Count, ObjectBytes = copied.Bytes, FilesPurgedMeanwhile = filesPurged };
                    }

                    await AddOwnerOnlyFileAsync(zip, snapshotPath, RootFolder.DatabaseFileName, cancellationToken);
                    foreach (string key in Directory.EnumerateFiles(root.KeysPath).Order(StringComparer.Ordinal))
                    {
                        await AddOwnerOnlyFileAsync(zip, key, $"{RootFolder.KeysFolderName}/{Path.GetFileName(key)}", cancellationToken);
                    }
                }

                File.Move(partialZip, target, overwrite: true);
                return summary;
            }
            finally
            {
                DeleteIfPresent(partialZip);
                DeleteIfPresent(snapshotPath);
            }
        }

        /// <summary>
        /// Copies the live database into a file readable by its owner only, since it holds password
        /// hashes: <c>VACUUM INTO</c> writes into an existing file when it is empty.
        /// </summary>
        private static async Task SnapshotAsync(RootFolder root, string snapshotPath)
        {
            await using (new FileStream(snapshotPath, OwnerOnly.Write(FileMode.CreateNew)))
            {
            }

            await using SqliteConnection live = new SqliteConnection(root.ConnectionString);
            await live.OpenAsync();
            await live.ExecuteAsync("VACUUM INTO @snapshotPath", new { snapshotPath });
        }

        /// <summary>
        /// Adds the object of every file in the snapshot. An object gone from disk is missing when the
        /// live database no longer points at it, purged or swapped out meanwhile, and lost otherwise.
        /// </summary>
        private static async Task<CopiedObjects> AddObjectsAsync(ZipArchive zip, RootFolder root, StorageDatabase snapshot, StorageDatabase live, CancellationToken cancellationToken)
        {
            ObjectStore objects = new ObjectStore(root);
            CopiedObjects copied = new CopiedObjects();
            foreach (string sha256 in await snapshot.Files.GetReferencedObjectsAsync())
            {
                string entryName = EntryName(root, objects, sha256);
                FileStream? source = TryOpenRead(objects, sha256);
                if (source == null && await live.Files.IsObjectReferencedAsync(sha256))
                {
                    // Opened again: a new upload of the same contents may have stored it after the first try.
                    // Untested: no test reaches that moment without a seam kept only for it.
                    source = TryOpenRead(objects, sha256)
                        ?? throw new InvalidDataException($"The object {entryName} is missing while a file points at it, so no backup was written. Restore it from a replica or an earlier backup.");
                }

                if (source == null)
                {
                    copied.Missing.Add(sha256);
                    continue;
                }

                await using (source)
                {
                    await AddOnceAsync(zip, root, objects, source, sha256, copied, cancellationToken);
                }
            }

            return copied;
        }

        /// <summary>
        /// Adds, for each live file in the snapshot whose object is missing because its contents were
        /// swapped meanwhile (encrypted or decrypted in place, which removes the old ones at once), the
        /// contents it holds now; answers those files, for the snapshot to take their new contents.
        /// </summary>
        /// <remarks>
        /// Only a live file's contents are swapped. One in the trash in the snapshot, restored and swapped
        /// since, is purged from the backup with the rest. So is one swapped again before its new contents
        /// were opened (untested: no test reaches that moment without a seam kept only for it).
        /// </remarks>
        private static async Task<IReadOnlyList<SwappedFile>> AddSwappedObjectsAsync(ZipArchive zip, RootFolder root, StorageDatabase snapshot, StorageDatabase live, CopiedObjects copied, CancellationToken cancellationToken)
        {
            ObjectStore objects = new ObjectStore(root);
            List<SwappedFile> swapped = new List<SwappedFile>();
            foreach (string sha256 in copied.Missing)
            {
                foreach (FileRecord file in await snapshot.Files.GetLiveWithObjectAsync(sha256))
                {
                    // The live database points at nothing missing (AddObjectsAsync), so a file still there
                    // holds other contents now; they are opened only when the zip does not hold them yet.
                    if (await live.Files.GetByIdAsync(file.Id) is not FileRecord current)
                    {
                        continue;
                    }

                    if (!copied.Added.Contains(current.Sha256))
                    {
                        if (TryOpenRead(objects, current.Sha256) is not FileStream source)
                        {
                            continue;
                        }

                        await using (source)
                        {
                            await AddOnceAsync(zip, root, objects, source, current.Sha256, copied, cancellationToken);
                        }
                    }

                    swapped.Add(new SwappedFile(file.Id, sha256, current));
                }
            }

            return swapped;
        }

        /// <summary>Adds the object in <paramref name="source"/> to the zip unless it holds it already, counting it in <paramref name="copied"/>.</summary>
        private static async Task AddOnceAsync(ZipArchive zip, RootFolder root, ObjectStore objects, FileStream source, string sha256, CopiedObjects copied, CancellationToken cancellationToken)
        {
            if (copied.Added.Add(sha256))
            {
                copied.Bytes += await AddObjectAsync(zip, EntryName(root, objects, sha256), source, sha256, cancellationToken);
            }
        }

        /// <summary>Where an object goes in the zip: its path under the root.</summary>
        private static string EntryName(RootFolder root, ObjectStore objects, string sha256)
        {
            return Path.GetRelativePath(root.Path, objects.GetPath(sha256)).Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>Copies one object into the zip, checking its SHA-256 as it goes; answers its size.</summary>
        private static async Task<long> AddObjectAsync(ZipArchive zip, string entryName, FileStream source, string sha256, CancellationToken cancellationToken)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            await using (Stream destination = await entry.OpenAsync(cancellationToken))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    size += read;
                }
            }

            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The object {entryName} does not match its SHA-256, so no backup was written. Restore it from a replica or an earlier backup.");
            }

            return size;
        }

        /// <summary>
        /// Drops from the snapshot the uploads in progress, points the files swapped meanwhile at their new
        /// contents, and purges the other files whose objects are missing, both logged so that replicas
        /// copying the restored root follow; answers how many files were purged.
        /// </summary>
        private static async Task<int> PruneSnapshotAsync(StorageDatabase snapshot, IReadOnlyList<SwappedFile> swapped, IReadOnlyList<string> missingObjects)
        {
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            await using IDbTransactionSession session = await snapshot.Sessions.CreateSessionWithTransactionAsync();
            foreach (Guid upload in await snapshot.Uploads.GetAllIdsAsync(session))
            {
                await snapshot.Uploads.DeleteAsync(upload, session);
            }

            foreach (SwappedFile file in swapped)
            {
                await snapshot.Files.SwapContentAsync(file.Id, file.OldSha256, file.Now.Sha256, file.Now.SizeBytes, file.Now.Encrypted, now, session);
            }

            int filesPurged = 0;
            foreach (string sha256 in missingObjects)
            {
                filesPurged += await snapshot.Files.PurgeAllWithObjectAsync(sha256, now, session);
            }

            await session.CommitAsync();
            return filesPurged;
        }

        private static async Task AddOwnerOnlyFileAsync(ZipArchive zip, string path, string entryName, CancellationToken cancellationToken)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

            // Readable by the owner only once unzipped: the database holds password hashes, keys/ the secrets.
            entry.ExternalAttributes = (int)OwnerOnly.FilePermissions << 16;
            await using FileStream source = File.OpenRead(path);
            await using Stream destination = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(destination, cancellationToken);
        }

        /// <summary>
        /// Takes <c>&lt;zip&gt;.lock</c> exclusively (on Linux an exclusive <c>flock</c>), removed when released.
        /// </summary>
        /// <exception cref="IOException">Another backup to <paramref name="target"/> is running.</exception>
        private static FileStream LockBackupPath(string target)
        {
            FileStreamOptions options = OwnerOnly.Write(FileMode.OpenOrCreate);
            options.Share = FileShare.None;
            options.Options = FileOptions.DeleteOnClose;
            try
            {
                return new FileStream(target + ".lock", options);
            }
            catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException)
            {
                throw new IOException($"Another backup to {target} is running.", exception);
            }
        }

        private static FileStream? TryOpenRead(ObjectStore objects, string sha256)
        {
            try
            {
                return objects.OpenRead(sha256);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        // Compares the paths as written, without resolving symbolic links: only an admin running the
        // backup can name a path through one, and the worst it does is put that one zip inside the root.
        private static bool IsInside(string path, string folder)
        {
            string relative = Path.GetRelativePath(folder, path);
            return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>The objects in the zip so far, their bytes, and those missing from disk.</summary>
        private sealed class CopiedObjects
        {
            public HashSet<string> Added { get; } = new HashSet<string>(StringComparer.Ordinal);

            public long Bytes { get; set; }

            public List<string> Missing { get; } = new List<string>();
        }

        /// <summary>A file in the snapshot that held <paramref name="OldSha256"/>, and its row in the live database now.</summary>
        private sealed record SwappedFile(Guid Id, string OldSha256, FileRecord Now);

        /// <summary>
        /// The storage repositories over one database file, outside the server's container. Private to the
        /// backup, its one user, as the repository tests build theirs by hand too.
        /// </summary>
        private sealed class StorageDatabase : IAsyncDisposable
        {
            private readonly DbDataSource _dataSource;

            public StorageDatabase(string connectionString)
            {
                _dataSource = DataSources.Create(connectionString);
                Sessions = new DbSessionFactory(_dataSource);
                Files = new FileRepository(_dataSource, Sessions, new ChangeLogRepository(_dataSource, Sessions));
                Uploads = new UploadRepository(_dataSource, Sessions);
            }

            public IDbSessionFactory Sessions { get; }

            public IFileRepository Files { get; }

            public IUploadRepository Uploads { get; }

            public ValueTask DisposeAsync()
            {
                return _dataSource.DisposeAsync();
            }
        }
    }
}
