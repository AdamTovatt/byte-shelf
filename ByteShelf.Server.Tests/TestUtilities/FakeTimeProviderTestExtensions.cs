using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Shared timing helpers for background-service tests driven by a <see cref="FakeTimeProvider"/>.
    /// </summary>
    public static class FakeTimeProviderTestExtensions
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Advances fake time in <paramref name="step"/> increments until <paramref name="signal"/>
        /// completes, giving each rescheduled iteration a brief real-time window to register its next
        /// timer. Gives up after ten seconds of real time, generous enough for a loaded machine that is
        /// slow to reach the timer, and throws when the signal never fired.
        /// </summary>
        public static async Task AdvanceUntilAsync(this FakeTimeProvider time, Task signal, TimeSpan step)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!signal.IsCompleted && elapsed.Elapsed < RealTimeLimit)
            {
                time.Advance(step);
                // The signal's own failure is left to the WaitAsync after the loop to throw.
                await signal.WaitAsync(TimeSpan.FromMilliseconds(20)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            await signal.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }
}
