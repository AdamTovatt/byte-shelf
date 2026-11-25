using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Storage.Models;

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

        /// <summary>What it registered to make derived files with; null when nothing.</summary>
        public PluginMakerRegistration? Maker { get; private set; }

        public void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader, new()
        {
            if (Reader != null)
            {
                throw new InvalidOperationException("A plugin has one reader at most.");
            }

            Reader = new PluginReaderRegistration(() => new TReader(), version, MediaTypeSet.Of(mediaTypes));
        }

        public void AddMaker<TMaker>(params IReadOnlyList<DerivedFileDeclaration> makes)
            where TMaker : class, IDerivedFileMaker, new()
        {
            if (Maker != null)
            {
                throw new InvalidOperationException("A plugin has one maker at most.");
            }

            if (makes.DistinctBy(declaration => declaration.Kind).Count() != makes.Count)
            {
                throw new ArgumentException("A kind of derived file is declared twice.", nameof(makes));
            }

            Maker = new PluginMakerRegistration(() => new TMaker(), makes.Select(DeclaredKind.Of).ToList());
        }
    }
}
