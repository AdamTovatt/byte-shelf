using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A logging provider for a running server that keeps each message its filter takes, formatted, from any
    /// thread. The app's own level filters still apply before it.
    /// </summary>
    public sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly Func<LoggedMessage, bool> _keep;

        /// <param name="keep">Which messages to keep, by their category, level, text and exception.</param>
        public RecordingLoggerProvider(Func<LoggedMessage, bool> keep)
        {
            _keep = keep;
        }

        public ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();

        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(this, categoryName);
        }

        public void Dispose()
        {
        }

        /// <summary>A message as the filter sees it.</summary>
        public sealed record LoggedMessage(string Category, LogLevel Level, string Text, Exception? Exception);

        private sealed class Logger : ILogger
        {
            private readonly RecordingLoggerProvider _provider;
            private readonly string _category;

            public Logger(RecordingLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
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
                string text = formatter(state, exception);
                if (_provider._keep(new LoggedMessage(_category, logLevel, text, exception)))
                {
                    _provider.Messages.Enqueue(text);
                }
            }
        }
    }
}
