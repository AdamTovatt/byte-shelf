using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Counts a worker's finished passes, which it logs, so a test can wait for one.</summary>
    internal sealed class PassLogger<TWorker> : ILogger<TWorker>
    {
        private readonly TaskCompletionSource[] _finished = [NewSignal(), NewSignal(), NewSignal()];
        private int _count;

        public Task Finished(int pass)
        {
            return _finished[pass - 1].Task;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("finished a pass", StringComparison.Ordinal))
            {
                int pass = Interlocked.Increment(ref _count);
                if (pass <= _finished.Length)
                {
                    _finished[pass - 1].TrySetResult();
                }
            }
        }

        private static TaskCompletionSource NewSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
