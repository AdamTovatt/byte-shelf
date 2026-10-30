namespace ByteShelf.Server.Shared.Concurrency
{
    /// <summary>
    /// An in-process async lock per key: two different keys never wait for each other, so a request
    /// streaming a slow body under one key holds up only the requests for that same key. A key's
    /// semaphore exists only while someone holds or waits for it. The server is one process, so this is
    /// enough to keep two requests from working on one object or upload at once.
    /// </summary>
    public sealed class KeyedAsyncLock
    {
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>How many keys are held or waited for; for tests.</summary>
        internal int ActiveKeyCount
        {
            get
            {
                lock (_entries)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>Waits for the lock on <paramref name="key"/>; dispose the answer to release it.</summary>
        public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            Entry entry;
            lock (_entries)
            {
                if (!_entries.TryGetValue(key, out Entry? existing))
                {
                    existing = new Entry();
                    _entries[key] = existing;
                }

                existing.Users++;
                entry = existing;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                Leave(key, entry);
                throw;
            }

            return new Releaser(this, key, entry);
        }

        private void Leave(string key, Entry entry)
        {
            lock (_entries)
            {
                entry.Users--;
                if (entry.Users == 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class Entry
        {
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

            /// <summary>Holders and waiters together; guarded by the dictionary's lock.</summary>
            public int Users { get; set; }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly KeyedAsyncLock _owner;
            private readonly string _key;
            private Entry? _entry;

            public Releaser(KeyedAsyncLock owner, string key, Entry entry)
            {
                _owner = owner;
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                Entry? entry = Interlocked.Exchange(ref _entry, null);
                if (entry == null)
                {
                    return;
                }

                entry.Semaphore.Release();
                _owner.Leave(_key, entry);
            }
        }
    }
}
