using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Contents whose derived files came or went, for open listings to be told, each once until taken.</summary>
    public sealed class DerivedChanges : IDisposable
    {
        private readonly Lock _lock = new Lock();
        private readonly WakeSignal _signal = new WakeSignal();
        private HashSet<string> _changed = new HashSet<string>(StringComparer.Ordinal);

        public void Add(string sha256)
        {
            lock (_lock)
            {
                _changed.Add(sha256);
            }

            _signal.Wake();
        }

        /// <summary>Those added since they were last taken, at once and perhaps none.</summary>
        public IReadOnlyCollection<string> Take()
        {
            lock (_lock)
            {
                HashSet<string> taken = _changed;
                _changed = new HashSet<string>(StringComparer.Ordinal);
                return taken;
            }
        }

        /// <summary>Those added since they were last taken, once there are any.</summary>
        public async Task<IReadOnlyCollection<string>> TakeAsync(TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            while (true)
            {
                IReadOnlyCollection<string> taken = Take();
                if (taken.Count > 0)
                {
                    return taken;
                }

                await _signal.WaitAsync(Timeout.InfiniteTimeSpan, timeProvider, cancellationToken);
            }
        }

        public void Dispose()
        {
            _signal.Dispose();
        }
    }
}
