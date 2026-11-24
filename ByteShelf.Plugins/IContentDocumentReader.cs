using System.Text.Json.Nodes;

namespace ByteShelf.Plugins
{
    /// <summary>
    /// A plugin's reader of files' contents (<see cref="IPluginBuilder.AddReader{TReader}"/>). The server runs
    /// it in the background over every object that a live file of its media types holds, never over an
    /// encrypted file's, and keeps what it answers as the plugin's document about the contents, which is gone
    /// when no plain file holds them any more. One is made for the server, and reads one object at a time.
    /// </summary>
    public interface IContentDocumentReader
    {
        /// <summary>
        /// What <paramref name="contents"/>, a seekable stream at its start, say; null for nothing. A read
        /// that throws, takes over 60 seconds or answers more than 64 KiB of JSON, or
        /// objects and arrays nested more than 32 deep, counts as nothing. At
        /// that time <paramref name="cancellationToken"/> is cancelled and <paramref name="contents"/> refuses
        /// to be read further; a read still running 5 seconds later is left behind, and the reader is not run
        /// again until the server restarts. What it throws is logged by its type alone. What it reads must be
        /// kept nowhere else: it would outlive the file it was read from being encrypted.
        /// </summary>
        Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken);
    }
}
