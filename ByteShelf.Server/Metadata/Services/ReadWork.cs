using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>What a plugin's process does for files' metadata: has the plugin's reader read the contents, and answers the document, or null.</summary>
    internal static class ReadWork
    {
        public const string Name = "read";

        /// <exception cref="PluginUnavailableException">The reader is another version.</exception>
        public static async Task<JsonElement> RunAsync(IContentDocumentReader reader, int version, JsonElement arguments, Stream contents)
        {
            return arguments.Deserialize<ReadArguments>(JsonDefaults.CamelCase)!.Version == version
                ? JsonSerializer.SerializeToElement(await reader.ReadAsync(contents))
                : throw PluginUnavailableException.OtherVersion();
        }
    }
}
