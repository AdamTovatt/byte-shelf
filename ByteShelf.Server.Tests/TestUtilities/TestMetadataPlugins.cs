using ByteShelf.Server.Metadata.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>The plugins a test says are installed. A test may set others, as a restart with others installed would.</summary>
    public sealed class TestMetadataPlugins : IMetadataPlugins
    {
        public TestMetadataPlugins(params MetadataPlugin[] plugins)
        {
            All = plugins;
        }

        public TestMetadataPlugins(PluginDeclaration declaration)
            : this(new MetadataPlugin(declaration))
        {
        }

        public IReadOnlyList<MetadataPlugin> All { get; set; }

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
            services.Replace(ServiceDescriptor.Singleton<IMetadataPlugins>(this));
        }
    }
}
