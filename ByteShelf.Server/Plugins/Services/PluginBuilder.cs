using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>What one plugin registers through.</summary>
    internal sealed class PluginBuilder : IPluginBuilder
    {
        public PluginBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }

        /// <summary>What it registered to read contents with; null when nothing.</summary>
        public PluginReaderRegistration? Reader { get; private set; }

        public void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader, new()
        {
            if (Reader != null)
            {
                throw new InvalidOperationException("A plugin has one reader at most.");
            }

            Reader = new PluginReaderRegistration(() => new TReader(), version, MediaTypeSet.Of(mediaTypes));
        }
    }
}
