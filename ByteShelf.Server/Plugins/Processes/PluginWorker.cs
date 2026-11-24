using System.Diagnostics.CodeAnalysis;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// A plugin's process with the contents it was given, used by one run at a time (<see cref="Lease"/>) until it is
    /// ended.
    /// </summary>
    [SuppressMessage("Design", "CA1001", Justification = "With no timer and no linked token its sources hold nothing, and whoever ends it again may still use them.")]
    internal sealed class PluginWorker
    {
        private readonly Lock _lock = new Lock();
        private readonly HashSet<string> _read = new HashSet<string>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly SemaphoreSlim _ending = new SemaphoreSlim(1, 1);
        private readonly PluginProcess _process;
        private Lease? _lease;
        private ITimer? _idle;
        private bool _stopped;
        private bool _ended;

        public PluginWorker(PluginTarget plugin, PluginProcess process)
        {
            Plugin = plugin;
            _process = process;
        }

        public PluginTarget Plugin { get; }

        public WorkFolder Folder => _process.Folder;

        /// <summary>Whether it was given the contents.</summary>
        public bool HasRead(string sha256)
        {
            lock (_lock)
            {
                return _read.Contains(sha256);
            }
        }

        /// <summary>
        /// Uses it for a run over the contents, which are noted before they are given; null when it can be used no
        /// more. Dispose the answer once nothing is being done in it, nor taken from its folder.
        /// </summary>
        /// <exception cref="PluginUnavailableException">The contents could not be noted. It is to be ended, as part of the note may be there, which the next would be taken for.</exception>
        public Lease? TryUse(string sha256)
        {
            lock (_lock)
            {
                // Its folder goes with the rest when somebody empties the cache it is in. One that has died tells itself.
                if (_stopped || _lease != null || !Directory.Exists(Folder.Path))
                {
                    return null;
                }

                if (!_read.Contains(sha256))
                {
                    try
                    {
                        Folder.Add(sha256);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        throw new PluginUnavailableException("the contents it is given could not be noted", exception);
                    }

                    _read.Add(sha256);
                }

                StopResting();
                return _lease = new Lease(this);
            }
        }

        /// <summary>Has it ended by <paramref name="end"/> once it has not been used for <paramref name="limit"/>.</summary>
        public void Rest(TimeProvider timeProvider, TimeSpan limit, Action end)
        {
            lock (_lock)
            {
                if (_lease == null && !_stopped)
                {
                    StopResting();
                    _idle = timeProvider.CreateTimer(_ => end(), null, limit, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>Has it be used no more, unless it is in use; answers whether it is not.</summary>
        public bool TryRetire()
        {
            lock (_lock)
            {
                if (_lease != null)
                {
                    return false;
                }

                StopResting();
                _stopped = true;
                return true;
            }
        }

        /// <summary>Ends it, with what it is doing, and removes its folder once what it answered last has been taken. Done again if it could not be.</summary>
        /// <exception cref="PluginNotStoppedException">It is still there, or its folder is.</exception>
        public async Task EndAsync()
        {
            Task released;
            lock (_lock)
            {
                StopResting();
                _stopped = true;
                released = _lease?.Released ?? Task.CompletedTask;
            }

            await _stop.CancelAsync();
            await released;
            await _ending.WaitAsync();
            try
            {
                if (_ended)
                {
                    return;
                }

                await _process.EndAsync();
                try
                {
                    Folder.Remove();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new PluginNotStoppedException($"What a plugin left in {Folder.Path} could not be removed.", exception);
                }

                _process.Dispose();
                _ended = true;
            }
            finally
            {
                _ending.Release();
            }
        }

        private void StopResting()
        {
            _idle?.Dispose();
            _idle = null;
        }

        /// <summary>One run's use of it.</summary>
        internal sealed class Lease : IDisposable
        {
            private readonly PluginWorker _worker;
            private readonly TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public Lease(PluginWorker worker)
            {
                _worker = worker;
            }

            public PluginProcess Process => _worker._process;

            /// <summary>Cancelled when the server ends the process, whose end then says nothing of what it was doing.</summary>
            public CancellationToken Stop => _worker._stop.Token;

            public Task Released => _released.Task;

            public void Dispose()
            {
                lock (_worker._lock)
                {
                    _worker._lease = null;
                }

                _released.TrySetResult();
            }
        }
    }
}
