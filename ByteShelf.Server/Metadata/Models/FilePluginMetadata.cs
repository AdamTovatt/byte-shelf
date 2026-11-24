using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// A <c>file_plugin_metadata</c> row: what a plugin keeps about a file, whatever its contents. It is kept
    /// when the file is encrypted in place, so it must hold nothing the plugin read from the file's plaintext.
    /// </summary>
    public sealed class FilePluginMetadata
    {
        /// <summary><see cref="IdFor"/> of <see cref="PluginId"/> and <see cref="FileId"/>.</summary>
        public required Guid Id { get; init; }

        public required string PluginId { get; init; }

        public required Guid FileId { get; init; }

        /// <summary>1 for a first write, one more for each after it, a removal included; a write names the one it replaces.</summary>
        public required int Version { get; init; }

        /// <summary>Null once the plugin removed it. The row stays for its version, which no later write repeats.</summary>
        public PluginDocument? Document { get; init; }

        public static Guid IdFor(string pluginId, Guid fileId)
        {
            return PluginMetadataId.For(pluginId, fileId.ToString("D"));
        }

        public static FilePluginMetadata Of(string pluginId, Guid fileId, int version, PluginDocument? document)
        {
            return new FilePluginMetadata { Id = IdFor(pluginId, fileId), PluginId = pluginId, FileId = fileId, Version = version, Document = document };
        }
    }
}
