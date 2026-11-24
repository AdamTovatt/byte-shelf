using System.Reflection;
using ByteShelf.Plugins;
using ByteShelf.Server.Plugins;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>That what plugins are written against and its version change together.</summary>
    public sealed class PluginContractTests
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly Assembly Contract = typeof(IByteShelfPlugin).Assembly;

        /// <summary>
        /// Changed with the lines here: the version, <c>AssemblyVersion</c> in ByteShelf.Plugins.csproj, when the
        /// contract had been released without the change (Plugins.md, Versions).
        /// </summary>
        [Fact]
        public void Contract_IsWhatItsVersionSays()
        {
            IEnumerable<string> members = Contract.GetExportedTypes().SelectMany(type => type.GetMembers(Declared)
                .Select(member => $"{type}: {member}")
                .Concat(type.GetInterfaces().Select(inherited => $"{type} is {inherited}"))
                .Append(type.ToString()));

            Assert.Equal(
                [
                    "ByteShelf.Plugins 1.0.0.0",
                    "ByteShelf.Plugins.IByteShelfPlugin",
                    "ByteShelf.Plugins.IByteShelfPlugin: Void Configure(ByteShelf.Plugins.IPluginBuilder)",
                    "ByteShelf.Plugins.IPluginBuilder",
                    "ByteShelf.Plugins.IPluginBuilder: Microsoft.Extensions.DependencyInjection.IServiceCollection Services",
                    "ByteShelf.Plugins.IPluginBuilder: Microsoft.Extensions.DependencyInjection.IServiceCollection get_Services()",
                ],
                members.Order(StringComparer.Ordinal).Prepend($"{Contract.GetName().Name} {Contract.GetName().Version}"));
        }

        /// <summary>A plugin built against a patch release of it would not load on a server that has the one before.</summary>
        [Fact]
        public void Common_VersionLeavesThePatchOut()
        {
            System.Version version = typeof(ShelfPath).Assembly.GetName().Version!;

            Assert.Equal(new System.Version(version.Major, version.Minor, 0, 0), version);
        }

        [Fact]
        public void Contract_MajorVersionIsTheApi()
        {
            Assert.Equal(PluginApi.Version, Contract.GetName().Version!.Major);
        }
    }
}
