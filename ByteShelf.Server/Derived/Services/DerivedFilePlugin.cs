using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>An installed plugin that makes derived files, as the server knows it: what it makes, but nothing that could make it here.</summary>
    /// <param name="Target">The plugin.</param>
    /// <param name="Makes">What it makes.</param>
    public sealed record DerivedFilePlugin(PluginTarget Target, IReadOnlyList<DeclaredKind> Makes)
    {
        /// <summary>The plugin's id, no other installed plugin's.</summary>
        public string Id => Target.Id;

        /// <summary>What it makes of <paramref name="group"/>.</summary>
        public IEnumerable<DeclaredKind> MakesIn(MakingGroup group)
        {
            return Makes.Where(declaration => DerivedKinds.GroupOf(declaration.Kind) == group);
        }

        /// <summary>Whether it makes any kind of <paramref name="group"/>.</summary>
        public bool MakesAny(MakingGroup group)
        {
            return MakesIn(group).Any();
        }
    }
}
