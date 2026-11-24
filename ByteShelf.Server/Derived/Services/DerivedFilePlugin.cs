using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>An installed plugin that makes derived files, as the server knows it: what it makes, but nothing that could make it here.</summary>
    public sealed class DerivedFilePlugin
    {
        /// <exception cref="ArgumentException">The id is not a plugin's, or a kind is declared twice.</exception>
        public DerivedFilePlugin(string id, string location, IEnumerable<DerivedFileDeclaration> makes)
        {
            if (!PluginDeclaration.IsId(id))
            {
                throw new ArgumentException($"'{id}' is not a plugin's id: lowercase letters, digits and hyphens.", nameof(id));
            }

            Id = id;
            Location = location;
            Makes = makes.ToList();

            if (Makes.DistinctBy(declaration => declaration.Kind).Count() != Makes.Count)
            {
                throw new ArgumentException($"The plugin '{id}' declares a kind of derived file twice.", nameof(makes));
            }
        }

        /// <summary>The plugin's id, no other installed plugin's.</summary>
        public string Id { get; }

        /// <summary>Where a child process finds the plugin again to load it (<see cref="IDerivedFilePlugins.Load"/>).</summary>
        public string Location { get; }

        public IReadOnlyList<DerivedFileDeclaration> Makes { get; }
    }
}
