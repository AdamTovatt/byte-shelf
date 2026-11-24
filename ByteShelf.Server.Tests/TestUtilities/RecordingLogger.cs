using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A logger that keeps each message it is given, formatted, and the exceptions given with them, for a test to read.</summary>
    public class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = new List<string>();

        public List<Exception> Exceptions { get; } = new List<Exception>();

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
            Messages.Add(formatter(state, exception));
            if (exception != null)
            {
                Exceptions.Add(exception);
            }
        }
    }

    /// <summary>A <see cref="RecordingLogger"/> to inject where an <see cref="ILogger{T}"/> is asked for.</summary>
    public sealed class RecordingLogger<T> : RecordingLogger, ILogger<T>
    {
    }
}
