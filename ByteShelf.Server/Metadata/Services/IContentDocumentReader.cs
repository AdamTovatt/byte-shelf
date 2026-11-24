using System.Text.Json.Nodes;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// A plugin's reader of files' contents. The server runs it in the background over every object that a
    /// live file of its media types holds, never over an encrypted file's, and keeps what it answers.
    /// </summary>
    public interface IContentDocumentReader
    {
        /// <summary>The reader's version: what another version read, or failed to read, is read again.</summary>
        int Version { get; }

        /// <summary>The media types it takes, lowercase: <c>image/png</c>, or a family as <c>image/*</c>.</summary>
        IReadOnlyList<string> MediaTypes { get; }

        /// <summary>
        /// What <paramref name="contents"/>, a seekable stream at its start, say; null for nothing. A read
        /// that throws, takes over 60 seconds or answers more than a document may hold counts as nothing. At
        /// that time <paramref name="cancellationToken"/> is cancelled and <paramref name="contents"/> refuses
        /// to be read further; a read still running 5 seconds later is left behind, and the reader is not run
        /// again until the server restarts. What it throws is logged by its type alone.
        /// </summary>
        Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken);
    }
}
