namespace ByteShelf.Server.Shared.Concurrency
{
    /// <summary>
    /// Lets a loop that waits between rounds be woken early: <see cref="WaitAsync"/> returns when
    /// <see cref="Wake"/> is called or the timeout passes. Waking with nobody waiting wakes the next wait.
    /// </summary>
    public sealed class WakeSignal : IDisposable
    {
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, 1);

        public void Wake()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already woken; once is enough.
            }
        }

        /// <summary>Waits until woken or <paramref name="timeout"/> passes.</summary>
        public async Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            using CancellationTokenSource delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task woken = _signal.WaitAsync(delay.Token);
            Task elapsed = Task.Delay(timeout, timeProvider, delay.Token);
            await Task.WhenAny(woken, elapsed);
            await delay.CancelAsync();

            try
            {
                await Task.WhenAll(woken, elapsed);
            }
            catch (OperationCanceledException)
            {
                // The one that did not finish first, cancelled above.
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            _signal.Dispose();
        }
    }
}
