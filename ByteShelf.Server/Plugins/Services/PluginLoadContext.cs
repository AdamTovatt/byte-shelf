using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// One plugin's assemblies, kept apart from the server's and from other plugins': its own packages
    /// and native libraries come from its folder, and <see cref="HostAssemblies"/> from the server.
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginId;
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginId, string assemblyPath)
            : base($"plugin {pluginId}")
        {
            _pluginId = pluginId;
            _resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        /// <summary>The id of the plugin that loaded <paramref name="assembly"/>; null for one of the server's.</summary>
        public static string? PluginIdOf(Assembly assembly)
        {
            return (GetLoadContext(assembly) as PluginLoadContext)?._pluginId;
        }

        /// <summary>
        /// What of <see cref="HostAssemblies"/> the assembly at <paramref name="path"/> references, and what
        /// the plugin's own assemblies do that its references lead to, read without loading any.
        /// </summary>
        public IEnumerable<AssemblyName> SharedReferencesFrom(string path)
        {
            Queue<string> unread = new Queue<string>([path]);
            HashSet<string> found = new HashSet<string>(unread);
            while (unread.TryDequeue(out string? next))
            {
                foreach (AssemblyName reference in ReferencesOf(next))
                {
                    if (HostAssemblies.Contains(reference.Name))
                    {
                        yield return reference;
                    }
                    else if (_resolver.ResolveAssemblyToPath(reference) is { } own && found.Add(own))
                    {
                        unread.Enqueue(own);
                    }
                }
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Null has the server's context load it.
            string? path = HostAssemblies.Contains(assemblyName.Name) ? null : _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? 0 : LoadUnmanagedDllFromPath(path);
        }

        private static List<AssemblyName> ReferencesOf(string path)
        {
            using PEReader file = new PEReader(File.OpenRead(path));
            MetadataReader metadata = file.GetMetadataReader();
            return metadata.AssemblyReferences.Select(handle => metadata.GetAssemblyReference(handle).GetAssemblyName()).ToList();
        }
    }
}
