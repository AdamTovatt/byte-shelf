using System.Collections.Frozen;
using ByteShelf.Plugins;
using ByteShelfCommon;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// The assemblies every plugin gets from the server, whatever copy it brings: the contract and the
    /// framework, whose types the two exchange, and <c>ByteShelfCommon</c>, whose rules for names, paths and
    /// keys are then the server's own. A break in any raises <see cref="PluginApi.Version"/>, and a
    /// plugin built against a later one than the server's is refused (<see cref="PluginLoader"/>). Any other
    /// package the server uses is not among them, so a plugin may bring its own version of it.
    /// </summary>
    internal static class HostAssemblies
    {
        private const string FrameworkListName = "framework-assemblies.txt";

        private static readonly FrozenSet<string> Names = ReadNames();

        public static bool Contains(string? simpleName)
        {
            return simpleName != null && Names.Contains(simpleName);
        }

        private static FrozenSet<string> ReadNames()
        {
            using Stream stream = typeof(HostAssemblies).Assembly.GetManifestResourceStream(FrameworkListName)
                ?? throw new InvalidOperationException($"{FrameworkListName} is not built into the server.");
            using StreamReader reader = new StreamReader(stream);

            return reader.ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Append(typeof(IByteShelfPlugin).Assembly.GetName().Name!)
                .Append(typeof(ShelfPath).Assembly.GetName().Name!)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
