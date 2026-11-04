using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Keeps every message logged through any of its loggers, whatever their category, formatted and in order.</summary>
    public sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly RecordingLogger _logger = new RecordingLogger();

        public List<string> Messages => _logger.Messages;

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }
}
