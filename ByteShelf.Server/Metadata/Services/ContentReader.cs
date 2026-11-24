using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Records what a reader found in the contents <paramref name="sha256"/>, in the caller's transaction;
    /// false, changing nothing, when no live plain file holds them any more or the reader's version has
    /// recorded them already.
    /// </summary>
    internal delegate Task<bool> RecordReading(string sha256, DateTimeOffset at, IDbSession session);

    /// <summary>A reader of contents, as <see cref="ContentReading"/> runs it: what it has yet to read, how it reads, and where what it finds goes.</summary>
    internal abstract class ContentReader
    {
        /// <summary>Whether it may read more: not once it has shown that it cannot, whatever the contents.</summary>
        public abstract bool MayRun { get; }

        /// <summary>
        /// The SHA-256 of contents it has yet to read: held by a live file that is not encrypted and is of a
        /// type it takes. Those after <paramref name="after"/>, in order, each once, at most <paramref name="limit"/>.
        /// </summary>
        public abstract Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit);

        /// <summary>
        /// Reads the contents <paramref name="sha256"/>. A read that failed records that they say nothing, so they are not
        /// read again. Null, with nothing to record, when the contents are gone, or were as it read them, or it could
        /// not run, which says nothing of them: <see cref="MayRun"/> is false from then on.
        /// </summary>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public abstract Task<ReadOutcome?> ReadAsync(string sha256, CancellationToken cancellationToken);
    }
}
