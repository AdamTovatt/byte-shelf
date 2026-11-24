using System.Text.Json;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Shared.Concurrency;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// The derived files on disk, at <c>derived/&lt;kind&gt;/ab/cd/&lt;sha256&gt;</c> with a <c>.json</c>
    /// (<see cref="DerivedEntry"/>) beside each, by the SHA-256 of the contents they were made from. A cache:
    /// emptying the folder has them made again.
    /// </summary>
    /// <remarks>
    /// What may be here depends on the database (a plain file holds the contents), so whoever writes or removes
    /// holds <see cref="LockAsync"/> from that question to the answer's commit. Only a swap, which holds the
    /// database's write lock too, waits for a second one while it holds one, and a holder may wait for what is
    /// being made from those contents to end, which waits for no other contents: so no two wait for each other.
    /// </remarks>
    public sealed class DerivedStore
    {
        /// <summary>How many bytes <see cref="DerivedKind.Full"/> files may take together.</summary>
        internal const long MaxFullBytes = 2L * 1024 * 1024 * 1024;

        private const string EntrySuffix = ".json";

        private readonly RootFolder _root;
        private readonly TimeProvider _timeProvider;
        private readonly KeyedAsyncLock _locks = new KeyedAsyncLock();

        public DerivedStore(RootFolder root, TimeProvider timeProvider)
        {
            _root = root;
            _timeProvider = timeProvider;
        }

        /// <summary>For tests that need a race to happen in a set order.</summary>
        internal KeyedAsyncLock Locks => _locks;

        /// <summary>Holds the lock for what is derived from one contents; dispose the answer to release it.</summary>
        public Task<IDisposable> LockAsync(string sha256, CancellationToken cancellationToken = default)
        {
            return _locks.AcquireAsync(sha256, cancellationToken);
        }

        public string GetPath(DerivedKind kind, string sha256)
        {
            return ObjectStore.PathIn(FolderOf(kind), sha256);
        }

        /// <summary>What is known of the derived file; null for nothing.</summary>
        public DerivedEntry? Find(DerivedKind kind, string sha256)
        {
            try
            {
                string path = GetPath(kind, sha256);
                using FileStream file = File.OpenRead(path + EntrySuffix);
                DerivedEntry? entry = JsonSerializer.Deserialize<DerivedEntry>(file, JsonDefaults.CamelCase);
                return entry?.Made == null || File.Exists(path) ? entry : null;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or JsonException)
            {
                return null;
            }
        }

        /// <summary>Opens the derived file, which counts as using it; null when it is not there.</summary>
        public FileStream? TryOpenRead(DerivedKind kind, string sha256)
        {
            string path = GetPath(kind, sha256);
            try
            {
                FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (kind == DerivedKind.Full)
                {
                    File.SetLastWriteTimeUtc(path, _timeProvider.GetUtcNow().UtcDateTime);
                }

                return file;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Keeps the file at <paramref name="sourcePath"/> as the derived file, or that there is none when it is
        /// null and <paramref name="entry"/> says making it failed. Hold the lock.
        /// </summary>
        public void Put(DerivedKind kind, string sha256, DerivedEntry entry, string? sourcePath)
        {
            string path = GetPath(kind, sha256);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // The entry goes first and comes last, so one is there only beside the file it is about.
            File.Delete(path + EntrySuffix);
            if (sourcePath == null)
            {
                File.Delete(path);
            }
            else
            {
                File.Move(sourcePath, path, overwrite: true);
                File.SetLastWriteTimeUtc(path, _timeProvider.GetUtcNow().UtcDateTime);
            }

            OwnerOnly.WriteThenRename(path + EntrySuffix, JsonSerializer.Serialize(entry, JsonDefaults.CamelCase));
        }

        /// <summary>Removes one derived file, or that making it failed. Hold the lock.</summary>
        public void Delete(DerivedKind kind, string sha256)
        {
            string path = GetPath(kind, sha256);
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                File.Delete(path + EntrySuffix);
                File.Delete(path + EntrySuffix + ".tmp");
                File.Delete(path);
            }
        }

        /// <summary>Removes everything derived from the contents. Hold the lock.</summary>
        public void Delete(string sha256)
        {
            foreach (DerivedKind kind in DerivedKinds.All)
            {
                Delete(kind, sha256);
            }
        }

        /// <summary>The contents something of <paramref name="kind"/> is kept of, whole or left by a crash, each once.</summary>
        public IEnumerable<string> EnumerateHashes(DerivedKind kind)
        {
            string folder = FolderOf(kind);
            if (!Directory.Exists(folder))
            {
                return [];
            }

            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetFileName(path).Split('.')[0])
                .Where(ObjectStore.IsValidHash)
                .Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// Removes the least recently used <see cref="DerivedKind.Full"/> files until the rest take at most
        /// <see cref="MaxFullBytes"/> together; answers how many. The most recently used one stays, whatever its
        /// size, and one whose lock is held, as it is in use: this waits for nobody.
        /// </summary>
        public int TrimFull()
        {
            DirectoryInfo folder = new DirectoryInfo(FolderOf(DerivedKind.Full));
            if (!folder.Exists)
            {
                return 0;
            }

            List<FileInfo> files = folder.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => ObjectStore.IsValidHash(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            int removed = 0;
            long bytes = 0;
            foreach (FileInfo file in files)
            {
                bytes += file.Length;
                if (bytes <= MaxFullBytes || file == files[0])
                {
                    continue;
                }

                using (IDisposable? held = _locks.TryAcquire(file.Name))
                {
                    if (held != null)
                    {
                        Delete(DerivedKind.Full, file.Name);
                        removed++;
                    }
                }
            }

            return removed;
        }

        private string FolderOf(DerivedKind kind)
        {
            return Path.Combine(_root.DerivedPath, DerivedKinds.NameOf(kind));
        }
    }
}
