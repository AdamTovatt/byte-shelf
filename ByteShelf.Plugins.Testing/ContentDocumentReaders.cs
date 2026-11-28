using System.Text.Json.Nodes;

namespace ByteShelf.Plugins.Testing
{
    /// <summary>A reader of files' contents, given them in memory, as a plugin's tests give it them.</summary>
    public static class ContentDocumentReaders
    {
        /// <summary>What <paramref name="reader"/> says of <paramref name="contents"/>, as a seekable stream that cannot be written.</summary>
        public static async Task<JsonObject?> ReadAsync(this IContentDocumentReader reader, byte[] contents)
        {
            ArgumentNullException.ThrowIfNull(reader);
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            return await reader.ReadAsync(stream);
        }
    }
}
