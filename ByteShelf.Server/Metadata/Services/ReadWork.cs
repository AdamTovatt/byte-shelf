using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>What a plugin's process does for files' metadata: has the plugin's reader read the contents, and answers the document, or null.</summary>
    internal sealed class ReadWork : ProcessWork
    {
        public override PluginWork For(PluginRegistrations registered)
        {
            Func<IContentDocumentReader> reader = Ready("reader", registered.Reader?.Make);
            return (arguments, contents, folder) => RunAsync(reader(), registered.Reader!.Version, arguments, contents);
        }

        /// <exception cref="PluginUnavailableException">The reader is another version.</exception>
        private static async Task<JsonElement> RunAsync(IContentDocumentReader reader, int version, JsonElement arguments, Stream contents)
        {
            return arguments.Deserialize<ReadArguments>(JsonDefaults.CamelCase)!.Version == version
                ? JsonSerializer.SerializeToElement(await reader.ReadAsync(contents))
                : throw PluginUnavailableException.OtherVersion();
        }
    }
}
