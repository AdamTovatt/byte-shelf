using ByteShelf.Server.Metadata.Repositories;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Every reader <see cref="MetadataReading"/> runs, in order: the server's own, then each plugin's.</summary>
    internal sealed class ContentReaders
    {
        public ContentReaders(IObjectMetadataRepository objectMetadata, IContentPluginMetadataRepository pluginMetadata, IPluginReaders plugins)
        {
            All =
            [
                new BuiltInContentReader(objectMetadata),
                .. plugins.All.Select(reader => new PluginContentReader(reader, pluginMetadata)),
            ];
        }

        public IReadOnlyList<ContentReader> All { get; }
    }
}
