using System.Collections.Frozen;
using ByteShelf.Plugins;
using ByteShelfCommon;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// The assemblies every plugin gets from the server, whatever copy it brings: the contract and
    /// <c>ByteShelfCommon</c>, whose types the two exchange, and the framework. Any other package the
    /// server uses is not among them, so a plugin may bring its own version of it.
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
