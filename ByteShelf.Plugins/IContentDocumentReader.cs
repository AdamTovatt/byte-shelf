using System.Text.Json.Nodes;

namespace ByteShelf.Plugins
{
    /// <summary>
    /// A plugin's reader of files' contents (<see cref="IPluginBuilder.AddReader{TReader}"/>). The server runs
    /// it in the background over every object that a live file of its media types holds, never over an
    /// encrypted file's, and keeps what it answers as the plugin's document about the contents, which is gone
    /// when no plain file holds them any more. It is made with no services in a child process of the server's,
    /// which may take 1.5 GiB of memory and may be ended at any time, with no warning, and reads one object at a time: it shares
    /// no state with the rest of its plugin, and contents that hang or crash it take nothing else down.
    /// </summary>
    public interface IContentDocumentReader
    {
        /// <summary>
        /// What <paramref name="contents"/>, a seekable stream at its start, each read of which is asked of the
        /// server, say; null for nothing. A read that throws, crashes, takes too much memory, is ended for taking
        /// over 60 seconds or answers more than 64 KiB of JSON, or objects and arrays nested more than 32 deep,
        /// counts as nothing. What it throws is logged by its type alone. What it reads must be kept nowhere
        /// else: it would outlive the file it was read from being encrypted.
        /// </summary>
        Task<JsonObject?> ReadAsync(Stream contents);
    }
}
