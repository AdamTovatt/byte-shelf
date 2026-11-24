using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Records what a reader found in the contents <paramref name="sha256"/>, in the caller's transaction;
    /// false, changing nothing, when no live plain file holds them any more or the reader's version has
    /// recorded them already.
    /// </summary>
    internal delegate Task<bool> RecordReading(string sha256, DateTimeOffset at, IDbSession session);

    /// <summary>One of the readers <see cref="MetadataReading"/> runs: what it has yet to read, how it reads, and where what it finds goes.</summary>
    internal abstract class ContentReader
    {
        /// <summary>Names the reader, different for every one.</summary>
        public abstract string Id { get; }

        /// <summary>Records that the contents say nothing, as for a read that failed, so they are not read again.</summary>
        public abstract RecordReading Nothing { get; }

        /// <summary>Makes it ready to read, once: done at once when it is. One that fails, or never ends, says nothing of any contents.</summary>
        public abstract Task PrepareAsync();

        /// <summary>
        /// The SHA-256 of contents it has yet to read: held by a live file that is not encrypted and is of a
        /// type it takes. Those after <paramref name="after"/>, in order, each once, at most <paramref name="limit"/>.
        /// </summary>
        public abstract Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit);

        /// <summary>Once <see cref="PrepareAsync"/> is done, reads <paramref name="contents"/>, a seekable stream at its start; answers how to record what it found.</summary>
        public abstract Task<RecordReading> ReadAsync(Stream contents, CancellationToken cancellationToken);
    }
}
