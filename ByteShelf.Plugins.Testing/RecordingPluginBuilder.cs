using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Plugins.Testing
{
    /// <summary>A builder a plugin is built with in its tests, which keeps what the plugin adds, to be held to what it declares.</summary>
    public sealed class RecordingPluginBuilder : IPluginBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        /// <summary>The type of its reader of files' contents; null when it adds none.</summary>
        public Type? Reader { get; private set; }

        /// <summary>The types of files its reader reads.</summary>
        public IReadOnlyList<string> Read { get; private set; } = [];

        public int ReaderVersion { get; private set; }

        /// <summary>The type of its maker of derived files; null when it adds none.</summary>
        public Type? Maker { get; private set; }

        /// <summary>What its maker says it makes.</summary>
        public IReadOnlyList<DerivedFileDeclaration> Makes { get; private set; } = [];

        public void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader, new()
        {
            Reader = typeof(TReader);
            Read = mediaTypes;
            ReaderVersion = version;
        }

        public void AddMaker<TMaker>(params IReadOnlyList<DerivedFileDeclaration> makes)
            where TMaker : class, IDerivedFileMaker, new()
        {
            Maker = typeof(TMaker);
            Makes = makes;
        }
    }
}
