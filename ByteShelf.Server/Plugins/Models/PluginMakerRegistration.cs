using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;

namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>What a plugin registered to make derived files with.</summary>
    /// <param name="Make">Makes the maker, running its constructor: in a child process alone.</param>
    /// <param name="Makes">What it makes.</param>
    public sealed record PluginMakerRegistration(Func<IDerivedFileMaker> Make, IReadOnlyList<DeclaredKind> Makes);
}
