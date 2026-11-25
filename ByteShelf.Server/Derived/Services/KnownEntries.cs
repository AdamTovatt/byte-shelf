using System.Collections.Concurrent;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What was read from disk of contents' derived files, for listings, which ask for many.</summary>
    internal sealed class KnownEntries
    {
        private readonly ContentsLocks _locks;
        private readonly int _max;
        private readonly ConcurrentDictionary<string, IReadOnlyDictionary<DerivedKind, DerivedEntry>> _known =
            new ConcurrentDictionary<string, IReadOnlyDictionary<DerivedKind, DerivedEntry>>(StringComparer.Ordinal);

        /// <param name="locks">Held by whoever changes what is read.</param>
        /// <param name="max">How many contents' are known at most, before all are forgotten.</param>
        public KnownEntries(ContentsLocks locks, int max)
        {
            _locks = locks;
            _max = max;
        }

        /// <summary>What <paramref name="read"/> answered when it was last asked for these contents.</summary>
        public IReadOnlyDictionary<DerivedKind, DerivedEntry> Get(string sha256, Func<IReadOnlyDictionary<DerivedKind, DerivedEntry>> read)
        {
            if (_known.TryGetValue(sha256, out IReadOnlyDictionary<DerivedKind, DerivedEntry>? known))
            {
                return known;
            }

            // Known only when nobody could change it meanwhile; whoever does forgets it.
            using IDisposable? held = _locks.TryLock(sha256);
            IReadOnlyDictionary<DerivedKind, DerivedEntry> found = read();
            if (held != null)
            {
                if (_known.Count >= _max)
                {
                    _known.Clear();
                }

                _known[sha256] = found;
            }

            return found;
        }

        /// <summary>Has it read anew. Hold the contents' lock.</summary>
        public void Forget(string sha256)
        {
            _known.TryRemove(sha256, out _);
        }
    }
}
