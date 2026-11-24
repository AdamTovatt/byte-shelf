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
                .Where(member => !IsGeneratedForARecord(type, member))
                .Select(member => $"{type}: {member}")
                .Concat((IsRecord(type) ? [] : type.GetInterfaces()).Select(inherited => $"{type} is {inherited}"))
                .Append(type.ToString()));

            Assert.Equal(
                [
                    "ByteShelf.Plugins 1.0.0.0",
                    "ByteShelf.Plugins.DerivedFileDeclaration",
                    "ByteShelf.Plugins.DerivedFileDeclaration: ByteShelfCommon.DerivedKind Kind",
                    "ByteShelf.Plugins.DerivedFileDeclaration: Int32 Version",
                    "ByteShelf.Plugins.DerivedFileDeclaration: System.Collections.Generic.IReadOnlyList`1[System.String] MediaTypes",
                    "ByteShelf.Plugins.DerivedFileDeclaration: Void .ctor(ByteShelfCommon.DerivedKind, Int32, System.Collections.Generic.IReadOnlyList`1[System.String])",
                    "ByteShelf.Plugins.DerivedFileLimits",
                    "ByteShelf.Plugins.DerivedFileLimits: Int64 MaxBytesOf(ByteShelfCommon.DerivedKind)",
                    "ByteShelf.Plugins.DerivedOriginal",
                    "ByteShelf.Plugins.DerivedOriginal: Int32 Height",
                    "ByteShelf.Plugins.DerivedOriginal: Int32 Width",
                    "ByteShelf.Plugins.DerivedOriginal: System.String MediaType",
                    "ByteShelf.Plugins.DerivedOriginal: Void .ctor(Int32, Int32, System.String)",
                    "ByteShelf.Plugins.IByteShelfPlugin",
                    "ByteShelf.Plugins.IByteShelfPlugin: Void Configure(ByteShelf.Plugins.IPluginBuilder)",
                    "ByteShelf.Plugins.IContentDocumentReader",
                    "ByteShelf.Plugins.IContentDocumentReader: System.Threading.Tasks.Task`1[System.Text.Json.Nodes.JsonObject] ReadAsync(System.IO.Stream)",
                    "ByteShelf.Plugins.IDerivedFileMaker",
                    "ByteShelf.Plugins.IDerivedFileMaker: System.Threading.Tasks.Task MakeAsync(System.IO.Stream, System.Collections.Generic.IReadOnlyList`1[ByteShelfCommon.DerivedKind], ByteShelf.Plugins.IMadeFiles)",
                    "ByteShelf.Plugins.IMadeFiles",
                    "ByteShelf.Plugins.IMadeFiles: System.Threading.Tasks.Task AddAsync(ByteShelfCommon.DerivedKind, System.String, ByteShelf.Plugins.DerivedOriginal, System.Func`2[System.IO.Stream,System.Threading.Tasks.Task])",
                    "ByteShelf.Plugins.IPluginBuilder",
                    "ByteShelf.Plugins.IPluginBuilder: Microsoft.Extensions.DependencyInjection.IServiceCollection Services",
                    "ByteShelf.Plugins.IPluginBuilder: Microsoft.Extensions.DependencyInjection.IServiceCollection get_Services()",
                    "ByteShelf.Plugins.IPluginBuilder: Void AddMaker[TMaker](System.Collections.Generic.IReadOnlyList`1[ByteShelf.Plugins.DerivedFileDeclaration])",
                    "ByteShelf.Plugins.IPluginBuilder: Void AddReader[TReader](Int32, System.Collections.Generic.IReadOnlyList`1[System.String])",
                ],
                members.Order(StringComparer.Ordinal).Prepend($"{Contract.GetName().Name} {Contract.GetName().Version}"));
        }

        private static bool IsRecord(Type type)
        {
            return type.GetMethod("<Clone>$") != null;
        }

        /// <summary>What every record has, and what its properties are read by; a property's type says the rest.</summary>
        private static bool IsGeneratedForARecord(Type type, MemberInfo member)
        {
            return IsRecord(type) && member is not (PropertyInfo or ConstructorInfo);
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
