using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Reaches the replica's sync while it runs: <see cref="Wake"/> starts its next step at once (as when it
    /// has just been paired), and <see cref="PauseSteps"/> stops the step in flight and any that start
    /// until it is disposed (as for a promotion, which must not wait for a whole copy to finish).
    /// </summary>
    public sealed class ReplicaSyncControl : IDisposable
    {
        private readonly WakeSignal _wake = new WakeSignal();
        private readonly object _lock = new object();
        private CancellationTokenSource _steps = new CancellationTokenSource();
        private int _pauses;

        /// <summary>Fires when steps are paused, and already has while they are; a step takes it as it starts.</summary>
        public CancellationToken StepToken
        {
            get
            {
                lock (_lock)
                {
                    return _steps.Token;
                }
            }
        }

        public void Wake()
        {
            _wake.Wake();
        }

        /// <summary>Waits until woken or <paramref name="timeout"/> passes.</summary>
        public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            return _wake.WaitAsync(timeout, timeProvider, cancellationToken);
        }

        /// <summary>Stops the step in flight and every step started until the answer is disposed.</summary>
        public IDisposable PauseSteps()
        {
            CancellationTokenSource paused;
            lock (_lock)
            {
                _pauses++;
                paused = _steps;
            }

            paused.Cancel();
            return new Resume(this);
        }

        public void Dispose()
        {
            _wake.Dispose();
            lock (_lock)
            {
                _steps.Dispose();
            }
        }

        private void ResumeSteps()
        {
            lock (_lock)
            {
                _pauses--;
                if (_pauses == 0)
                {
                    // The cancelled source is not disposed: a step may still be linking to its token.
                    _steps = new CancellationTokenSource();
                }
            }
        }

        private sealed class Resume : IDisposable
        {
            private ReplicaSyncControl? _control;

            public Resume(ReplicaSyncControl control)
            {
                _control = control;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _control, null)?.ResumeSteps();
            }
        }
    }
}
