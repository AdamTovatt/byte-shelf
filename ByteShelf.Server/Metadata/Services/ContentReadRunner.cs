using System.Collections.Concurrent;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Runs one reader over one object, for as long as a read may take. A reader that keeps running past that
    /// though cancelled is left behind: its read still holds a thread, and the reader is not run again in this
    /// process, so each holds at most one.
    /// </summary>
    internal sealed class ContentReadRunner
    {
        /// <summary>How long a reader may take over one object.</summary>
        private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(60);

        /// <summary>How long a reader may take to stop once cancelled, before it is left behind.</summary>
        private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(5);

        /// <summary>The reads a stream gets buffered by: the object store's own streams read unbuffered, and the parsers read a few bytes at a time.</summary>
        private const int ReadBufferBytes = 64 * 1024;

        private readonly ConcurrentDictionary<string, bool> _leftBehind = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ObjectStore _objectStore;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ContentReadRunner> _logger;

        public ContentReadRunner(ObjectStore objectStore, TimeProvider timeProvider, ILogger<ContentReadRunner> logger)
        {
            _objectStore = objectStore;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>Whether <paramref name="reader"/> is left behind, and so not to be run again.</summary>
        public bool IsLeftBehind(ContentReader reader)
        {
            return _leftBehind.ContainsKey(reader.Id);
        }

        /// <summary>
        /// Runs <paramref name="reader"/> over the contents <paramref name="sha256"/>. A read that failed
        /// records that they say nothing, so they are not read again. Null when the object is gone, removed
        /// with the last file holding it after it was listed, which leaves nothing to record.
        /// </summary>
        /// <exception cref="OperationCanceledException">The server is shutting down, which says nothing of the reader.</exception>
        public async Task<ReadOutcome?> ReadAsync(ContentReader reader, string sha256, CancellationToken cancellationToken)
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
            Task<RecordReading> reading = Task.Run(() => ReadFileAsync(reader, file, limit.Token), CancellationToken.None);
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
                        "The reader {Reader} took over {Limit} to read the contents {Sha256}; they are recorded as carrying nothing.", reader.Id, ReadLimit, sha256);
                }
                else
                {
                    _leftBehind[reader.Id] = true;
                    _logger.LogError(
                        "The reader {Reader} did not stop reading the contents {Sha256} when cancelled. The read is left behind, and the reader is not run again until the server restarts.",
                        reader.Id,
                        sha256);
                }

                return new ReadOutcome(reader.Nothing, Failed: true);
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
                    "The reader {Reader} could not read the contents {Sha256} ({Exception}); they are recorded as carrying nothing.", reader.Id, sha256, exception.GetType().FullName);
                return new ReadOutcome(reader.Nothing, Failed: true);
            }
            finally
            {
                if (reading.IsCompleted)
                {
                    limit.Dispose();
                }
            }
        }

        /// <summary>Whether <paramref name="reading"/> ends, however it does, before <paramref name="limit"/> has passed.</summary>
        /// <exception cref="OperationCanceledException">The server is shutting down; the reading is left to end on its own.</exception>
        private async Task<bool> EndsWithinAsync(Task reading, TimeSpan limit, CancellationToken cancellationToken)
        {
            await reading.WaitAsync(limit, _timeProvider, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellationToken.ThrowIfCancellationRequested();
            return reading.IsCompleted;
        }

        /// <summary>Reads <paramref name="file"/> and closes it, whenever that ends: a reading left behind still holds it.</summary>
        private static async Task<RecordReading> ReadFileAsync(ContentReader reader, FileStream file, CancellationToken cancellationToken)
        {
            await using (Stream contents = new CancellableReadStream(new BufferedStream(file, ReadBufferBytes), cancellationToken))
            {
                return await reader.ReadAsync(contents, cancellationToken);
            }
        }
    }
}
