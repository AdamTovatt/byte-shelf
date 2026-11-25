using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Runs the server's own reader over one object in the server's process, for as long as a read may take. When it
    /// keeps running past its time though cancelled it is left behind: its read still holds a thread, and it is not
    /// run again in this process, so it holds at most one.
    /// </summary>
    internal sealed class ContentReadRunner
    {
        /// <summary>How long a reader may take over one object.</summary>
        private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(60);

        /// <summary>How long a reader may take to stop once cancelled, before it is left behind.</summary>
        private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(5);

        /// <summary>The reads a stream gets buffered by: the object store's own streams read unbuffered, and the parsers read a few bytes at a time.</summary>
        private const int ReadBufferBytes = 64 * 1024;

        private readonly ObjectStore _objectStore;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ContentReadRunner> _logger;
        private volatile bool _leftBehind;

        public ContentReadRunner(ObjectStore objectStore, TimeProvider timeProvider, ILogger<ContentReadRunner> logger)
        {
            _objectStore = objectStore;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>Whether the reader may run; not once it was left behind.</summary>
        public bool MayRun => !_leftBehind;

        /// <summary>
        /// Runs <paramref name="read"/> over the contents <paramref name="sha256"/>. A read that failed answers
        /// <paramref name="nothing"/>, so they are not read again. Null when the object is gone, removed with the last
        /// file holding it after it was listed, which leaves nothing to record.
        /// </summary>
        /// <exception cref="OperationCanceledException">The server is shutting down, which says nothing of the reader.</exception>
        public async Task<ReadOutcome?> ReadAsync(string sha256, Func<Stream, RecordReading> read, RecordReading nothing, CancellationToken cancellationToken)
        {
            FileStream file;
            try
            {
                file = _objectStore.OpenRead(sha256);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }

            CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<RecordReading> reading = Task.Run(() => ReadFile(read, file, limit.Token), CancellationToken.None);
            try
            {
                if (await EndsWithinAsync(reading, ReadLimit, cancellationToken))
                {
                    return new ReadOutcome(await reading, Failed: false);
                }

                // Not awaited: what a reader registered on the token is its own, and may throw or block.
                _ = limit.CancelAsync();
                if (await EndsWithinAsync(reading, StopLimit, cancellationToken))
                {
                    _logger.LogWarning(
                        "Reading the contents {Sha256} took over {Limit}; they are recorded as carrying nothing.", sha256, ReadLimit);
                }
                else
                {
                    _leftBehind = true;
                    _logger.LogError(
                        "The read of the contents {Sha256} did not stop when cancelled. It is left behind, and no more are read until the server restarts.", sha256);
                }

                return new ReadOutcome(nothing, Failed: true);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The parsers throw whatever damaged contents lead them to, truncated ones included as
                // IOException subtypes (BufferBoundsException, EndOfStreamException), so I/O errors are
                // not let through to be retried: every damaged file would then fail every pass. Contents
                // are written once and never change, so reading them again would fail the same way;
                // a passing disk error on a local file is too rare to tell apart.
                // The type alone: a message may quote what was read, which must not outlive it in the server's log.
                _logger.LogInformation(
                    "The contents {Sha256} could not be read ({Exception}); they are recorded as carrying nothing.", sha256, exception.GetType().FullName);
                return new ReadOutcome(nothing, Failed: true);
            }
            finally
            {
                if (reading.IsCompleted)
                {
                    limit.Dispose();
                }
            }
        }

        /// <summary>Whether <paramref name="work"/>, a reader's, ends, however it does, before <paramref name="limit"/> has passed.</summary>
        /// <exception cref="OperationCanceledException">The server is shutting down; the work is left to end on its own.</exception>
        private async Task<bool> EndsWithinAsync(Task work, TimeSpan limit, CancellationToken cancellationToken)
        {
            await work.WaitAsync(limit, _timeProvider, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellationToken.ThrowIfCancellationRequested();
            return work.IsCompleted;
        }

        /// <summary>Reads <paramref name="file"/> and closes it, whenever that ends: a reading left behind still holds it.</summary>
        private static RecordReading ReadFile(Func<Stream, RecordReading> read, FileStream file, CancellationToken cancellationToken)
        {
            using (Stream contents = new CancellableReadStream(new BufferedStream(file, ReadBufferBytes), cancellationToken))
            {
                return read(contents);
            }
        }
    }
}
