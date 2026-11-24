using System.Text.Json;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
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
    /// holds the contents' lock (<see cref="ContentsLocks"/>) from that question to the answer's commit.
    /// </remarks>
    public sealed class DerivedStore
    {
        /// <summary>How many bytes <see cref="DerivedKind.Full"/> files may take together.</summary>
        internal const long MaxFullBytes = 2L * 1024 * 1024 * 1024;

        private const int MaxKnown = 100_000;

        private const string EntrySuffix = ".json";

        private readonly RootFolder _root;
        private readonly TimeProvider _timeProvider;
        private readonly ContentsLocks _locks;
        private readonly KnownEntries _known;
        private readonly DerivedChanges _changes;

        public DerivedStore(RootFolder root, ContentsLocks locks, DerivedChanges changes, TimeProvider timeProvider)
        {
            _root = root;
            _locks = locks;
            _changes = changes;
            _timeProvider = timeProvider;
            _known = new KnownEntries(locks, MaxKnown);
        }

        public string GetPath(DerivedKind kind, string sha256)
        {
            return ObjectStore.PathIn(FolderOf(kind), sha256);
        }

        /// <summary>What is known of the derived file; null for nothing, also when that cannot be read.</summary>
        public DerivedEntry? Find(DerivedKind kind, string sha256)
        {
            string path = GetPath(kind, sha256);

            // Asked first, as an exception costs far more, and most files have none.
            if (!File.Exists(path + EntrySuffix))
            {
                return null;
            }

            try
            {
                using FileStream file = File.OpenRead(path + EntrySuffix);
                DerivedEntry? entry = JsonSerializer.Deserialize<DerivedEntry>(file, JsonDefaults.CamelCase);
                return entry?.Made == null || File.Exists(path) ? entry : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// What is known of the kinds derived from the contents, from memory once it has been read from disk. One
        /// somebody empties the folder of may still be answered.
        /// </summary>
        public IReadOnlyDictionary<DerivedKind, DerivedEntry> FindAll(string sha256)
        {
            return _known.Get(sha256, () => DerivedKinds.All
                .Select(kind => (Kind: kind, Entry: Find(kind, sha256)))
                .Where(found => found.Entry != null)
                .ToDictionary(found => found.Kind, found => found.Entry!));
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

        /// <summary>Keeps what was made of the contents in one run. Hold the lock.</summary>
        public void Put(string sha256, params IReadOnlyList<KeptFile> kept)
        {
            Change(sha256, () => kept.Select(file => Put(sha256, file)).ToList().Contains(true));
        }

        /// <summary>Removes one derived file, or that making it failed. Hold the lock.</summary>
        public void Delete(DerivedKind kind, string sha256)
        {
            Change(sha256, () => Delete(sha256, kind));
        }

        /// <summary>Removes everything derived from the contents. Hold the lock.</summary>
        public void Delete(string sha256)
        {
            Change(sha256, () => DerivedKinds.All.Select(kind => Delete(sha256, kind)).ToList().Contains(true));
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

                using (IDisposable? held = _locks.TryLock(file.Name))
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

        /// <summary>Changes the contents' derived files on disk, and has listings say so when it answers that one came or went, or fails, as after some.</summary>
        private void Change(string sha256, Func<bool> change)
        {
            _known.Forget(sha256);
            bool changed = true;
            try
            {
                changed = change();
            }
            finally
            {
                if (changed)
                {
                    _changes.Add(sha256);
                }
            }
        }

        private bool Put(string sha256, KeptFile kept)
        {
            string path = GetPath(kept.Kind, sha256);
            bool wasThere = File.Exists(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // The entry goes first and comes last, so one is there only beside the file it is about.
            File.Delete(path + EntrySuffix);
            if (kept.SourcePath == null)
            {
                File.Delete(path);
            }
            else
            {
                File.Move(kept.SourcePath, path, overwrite: true);
                File.SetLastWriteTimeUtc(path, _timeProvider.GetUtcNow().UtcDateTime);
            }

            OwnerOnly.WriteThenRename(path + EntrySuffix, JsonSerializer.Serialize(kept.Entry, JsonDefaults.CamelCase));
            return wasThere || kept.SourcePath != null;
        }

        private bool Delete(string sha256, DerivedKind kind)
        {
            string path = GetPath(kind, sha256);
            if (!Directory.Exists(Path.GetDirectoryName(path)))
            {
                return false;
            }

            bool wasThere = File.Exists(path);
            File.Delete(path + EntrySuffix);
            File.Delete(path + EntrySuffix + ".tmp");
            File.Delete(path);
            return wasThere;
        }

        private string FolderOf(DerivedKind kind)
        {
            return Path.Combine(_root.DerivedPath, DerivedKinds.NameOf(kind));
        }
    }
}
