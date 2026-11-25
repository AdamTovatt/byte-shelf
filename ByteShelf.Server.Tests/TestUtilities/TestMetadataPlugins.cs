using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A plugin a test says is installed, with its reader if it has one.</summary>
    public sealed record TestMetadataPlugin(PluginDeclaration Declaration, TestDocumentReader? Reader = null);

    /// <summary>The plugins a test says are installed. Others set since are what services made after have, as after a restart.</summary>
    public sealed class TestMetadataPlugins
    {
        public TestMetadataPlugins(params TestMetadataPlugin[] plugins)
        {
            All = plugins;
        }

        public TestMetadataPlugins(PluginDeclaration declaration)
            : this(new TestMetadataPlugin(declaration))
        {
        }

        public IReadOnlyList<TestMetadataPlugin> All { get; set; }

        /// <summary>A plugin with a field of every kind in its document about a file, and two in the one about its contents.</summary>
        public static PluginDeclaration Declaration { get; } = new PluginDeclaration(
            TestPluginMetadata.Plugin,
            "Test plugin",
            [
                new PluginField("caption", "Caption", PluginFieldKind.Text, PluginDocumentSubject.File, searched: true, listed: true),
                new PluginField("note", "Note", PluginFieldKind.Text, PluginDocumentSubject.File),
                new PluginField("rating", "Rating", PluginFieldKind.Number, PluginDocumentSubject.File, listed: true),
                new PluginField("due", "Due", PluginFieldKind.Date, PluginDocumentSubject.File, listed: true),
                new PluginField("done", "Done", PluginFieldKind.Flag, PluginDocumentSubject.File, listed: true),
                new PluginField("words", "Words", PluginFieldKind.Text, PluginDocumentSubject.Contents, searched: true, listed: true),
                new PluginField("width", "Width", PluginFieldKind.Number, PluginDocumentSubject.Contents),
            ]);

        /// <summary>The filters <paramref name="filters"/> are written as.</summary>
        public static IReadOnlyList<PluginFieldFilter> Filters(params string[] filters)
        {
            return filters.Select(PluginFieldFilter.Parse).ToList();
        }

        /// <summary>Installs these plugins in place of none.</summary>
        public void AddTo(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Transient(_ => new InstalledPlugins(All.Select(plugin => plugin.Declaration))));
            services.Replace(ServiceDescriptor.Transient(provider => KnownPlugins.Of(provider.GetRequiredService<InstalledPlugins>())));
            services.Replace(ServiceDescriptor.Singleton<IPluginReaders>(provider => new Readers(this, provider.GetRequiredService<ObjectStore>())));
        }

        private sealed class Readers : IPluginReaders
        {
            private readonly TestMetadataPlugins _plugins;
            private readonly ObjectStore _objectStore;

            public Readers(TestMetadataPlugins plugins, ObjectStore objectStore)
            {
                _plugins = plugins;
                _objectStore = objectStore;
            }

            public IReadOnlyList<PluginReader> All => _plugins.All
                .Where(plugin => plugin.Reader != null)
                .Select(plugin => plugin.Reader!.AsReaderOf(plugin.Declaration.Id, _objectStore))
                .ToList();
        }
    }
}
