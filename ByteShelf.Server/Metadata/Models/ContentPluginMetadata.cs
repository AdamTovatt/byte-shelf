using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// A <c>content_plugin_metadata</c> row: what a plugin's reader found in the contents with SHA-256
    /// <see cref="Sha256"/>, for every file that holds them and is not encrypted.
    /// </summary>
    public sealed class ContentPluginMetadata
    {
        /// <summary><see cref="IdFor"/> of <see cref="PluginId"/> and <see cref="Sha256"/>.</summary>
        public required Guid Id { get; init; }

        public required string PluginId { get; init; }

        public required string Sha256 { get; init; }

        /// <summary>The version of the plugin's reader that read the contents; another version reads them again.</summary>
        public required int ReaderVersion { get; init; }

        /// <summary>When the contents were read.</summary>
        public required DateTimeOffset ReadAt { get; init; }

        /// <summary>What was found; null for nothing, or a read that failed.</summary>
        public PluginDocument? Document { get; init; }

        public static Guid IdFor(string pluginId, string sha256)
        {
            return PluginMetadataId.For(pluginId, sha256);
        }

        public static ContentPluginMetadata Of(string pluginId, string sha256, int readerVersion, DateTimeOffset readAt, PluginDocument? document)
        {
            return new ContentPluginMetadata
            {
                Id = IdFor(pluginId, sha256),
                PluginId = pluginId,
                Sha256 = sha256,
                ReaderVersion = readerVersion,
                ReadAt = readAt,
                Document = document,
            };
        }
    }
}
