using ByteShelf.Server.Metadata.Repositories;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Every reader <see cref="MetadataReading"/> runs, in order: the server's own, then each plugin's.</summary>
    internal sealed class ContentReaders
    {
        public ContentReaders(IObjectMetadataRepository objectMetadata, IContentPluginMetadataRepository pluginMetadata, IMetadataPlugins plugins)
        {
            All =
            [
                new BuiltInContentReader(objectMetadata),
                .. plugins.All
                    .Where(plugin => plugin.Reader != null)
                    .Select(plugin => new PluginContentReader(plugin.Declaration.Id, plugin.Reader!, pluginMetadata)),
            ];
        }

        public IReadOnlyList<ContentReader> All { get; }
    }
}
