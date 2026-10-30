using System.Text.RegularExpressions;
using ByteShelf.Server.Shared.Concurrency;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The stored contents: one plain file per distinct content, at <c>objects/ab/cd/&lt;sha256&gt;</c>,
    /// written once and never modified, so <c>sha256sum</c> verifies every one of them.
    /// </summary>
    /// <remarks>
    /// Whether an object may be removed depends on the database (no file row points at it), and a
    /// finishing upload may be about to point a new row at it. Both sides therefore hold
    /// <see cref="LockAsync"/> for the hash across their database work: the upload from placing the
    /// object until its row commits, the removal from checking for rows until the file is gone.
    /// </remarks>
    public sealed partial class ObjectStore
    {
        private readonly RootFolder _root;
        private readonly KeyedAsyncLock _locks = new KeyedAsyncLock();

        public ObjectStore(RootFolder root)
        {
            _root = root;
        }

        public static bool IsValidHash(string sha256)
        {
            return HashPattern().IsMatch(sha256);
        }

        public string GetPath(string sha256)
        {
            if (!IsValidHash(sha256))
            {
                throw new ArgumentException($"'{sha256}' is not a lowercase hex SHA-256.", nameof(sha256));
            }

            return Path.Combine(_root.ObjectsPath, sha256[..2], sha256[2..4], sha256);
        }

        public bool Exists(string sha256)
        {
            return File.Exists(GetPath(sha256));
        }

        /// <summary>Holds the lock for one object; dispose the answer to release it.</summary>
        public Task<IDisposable> LockAsync(string sha256, CancellationToken cancellationToken = default)
        {
            return _locks.AcquireAsync(sha256, cancellationToken);
        }

        /// <summary>
        /// Moves a fully written and hashed file into place as the object <paramref name="sha256"/>. When
        /// the object already exists the contents are identical, so the source is deleted instead. Hold
        /// <see cref="LockAsync"/> for the hash.
        /// </summary>
        public void Place(string sourcePath, string sha256)
        {
            string path = GetPath(sha256);

            if (File.Exists(path))
            {
                File.Delete(sourcePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(sourcePath, path, overwrite: false);
        }

        public FileStream OpenRead(string sha256)
        {
            return new FileStream(GetPath(sha256), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        /// <summary>Removes the object when it exists. Hold <see cref="LockAsync"/> for the hash.</summary>
        public void Delete(string sha256)
        {
            string path = GetPath(sha256);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Every object on disk, by hash; anything else under <c>objects/</c> is skipped.</summary>
        public IEnumerable<string> EnumerateHashes()
        {
            foreach (string path in Directory.EnumerateFiles(_root.ObjectsPath, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (IsValidHash(name) && path == GetPath(name))
                {
                    yield return name;
                }
            }
        }

        [GeneratedRegex("^[0-9a-f]{64}$")]
        private static partial Regex HashPattern();
    }
}
