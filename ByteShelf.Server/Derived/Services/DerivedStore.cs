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
    /// (<see cref="DerivedEntry"/>) beside each, by the SHA-256 of the contents they were made from. A cache: emptying the
    /// folder has them made again. What writes them writes the bytes each takes with them (<see cref="DerivedFiles"/>).
    /// </summary>
    /// <remarks>
    /// What may be here depends on the database (a plain file holds the contents), so whoever writes or removes holds the
    /// contents' lock (<see cref="ContentsLocks"/>) from that question to the answer's commit
    /// (<see cref="ContentsTransactions"/>).
    /// </remarks>
    public sealed class DerivedStore
    {
        /// <summary>How many bytes <see cref="DerivedKind.Full"/> files may take together.</summary>
        internal const long MaxFullBytes = 2L * 1024 * 1024 * 1024;

        private const int MaxKnown = 100_000;

        private const string EntrySuffix = ".json";

        private readonly RootFolder _root;
        private readonly TimeProvider _timeProvider;
        private readonly KnownEntries _known;
        private readonly DerivedChanges _changes;

        public DerivedStore(RootFolder root, ContentsLocks locks, DerivedChanges changes, TimeProvider timeProvider)
        {
            _root = root;
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
                return entry?.Outcome is not DerivedOutcome.Made || File.Exists(path) ? entry : null;
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

        /// <summary>Puts what is to be kept of the contents in place of what was there of each kind. Hold the lock.</summary>
        public void Put(string sha256, IReadOnlyList<KeptFile> kept)
        {
            Change(sha256, () => kept.Select(file => PutOne(sha256, file)).ToList().Contains(true));
        }

        /// <summary>Removes one derived file, or what is known of making it. Hold the lock.</summary>
        public void Delete(DerivedKind kind, string sha256)
        {
            Change(sha256, () => DeleteOne(kind, sha256));
        }

        /// <summary>Removes everything derived from the contents. Hold the lock.</summary>
        public void Delete(string sha256)
        {
            Change(sha256, () => DerivedKinds.All.Select(kind => DeleteOne(kind, sha256)).ToList().Contains(true));
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
        /// The contents whose <see cref="DerivedKind.Full"/> file is past <see cref="MaxFullBytes"/>, counted from the most
        /// recently used, least recently used last. The most recently used one is never among them, whatever its size.
        /// </summary>
        public IReadOnlyList<FullFile> GetFullOverTheLimit()
        {
            DirectoryInfo folder = new DirectoryInfo(FolderOf(DerivedKind.Full));
            if (!folder.Exists)
            {
                return [];
            }

            List<FileInfo> files = folder.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => ObjectStore.IsValidHash(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            List<FullFile> over = [];
            long bytes = 0;
            foreach (FileInfo file in files)
            {
                bytes += file.Length;
                if (bytes > MaxFullBytes && file != files[0])
                {
                    over.Add(new FullFile(file.Name, file.LastWriteTimeUtc));
                }
            }

            return over;
        }

        /// <summary>When the <see cref="DerivedKind.Full"/> file kept of the contents was last used; null when none is.</summary>
        public DateTime? LastUsedOfFull(string sha256)
        {
            FileInfo file = new FileInfo(GetPath(DerivedKind.Full, sha256));
            return file.Exists ? file.LastWriteTimeUtc : null;
        }

        /// <summary>How many bytes the derived file kept takes; null when none is.</summary>
        public long? SizeOf(DerivedKind kind, string sha256)
        {
            if (Find(kind, sha256)?.Outcome is not DerivedOutcome.Made)
            {
                return null;
            }

            try
            {
                return new FileInfo(GetPath(kind, sha256)).Length;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
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

        /// <summary>Puts one kind in place; answers whether a listing would change, as a file came or went.</summary>
        private bool PutOne(string sha256, KeptFile kept)
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

        /// <summary>Removes one kind; answers whether a listing would change, as a file went.</summary>
        private bool DeleteOne(DerivedKind kind, string sha256)
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
