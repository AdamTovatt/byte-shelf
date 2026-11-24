using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ByteShelf.Server.Shared.Database;
using DbUp;
using DbUp.Sqlite;
using EasyReasy.Database.Sqlite;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>
    /// Databases are opened in one place, checked in the compiled code, where every way of writing a call looks
    /// the same, rather than by an analyzer, which would be a package for this one rule.
    /// </summary>
    public sealed class DatabaseOpeningTests
    {
        /// <summary>
        /// A connection <see cref="DatabaseFile"/> did not make has none of its settings, so nothing else in
        /// the server makes one, nor in these tests, which open the same databases, but for
        /// <see cref="OpeningWays"/>: every way there is, for the check to find.
        /// </summary>
        [Fact]
        public void Assemblies_OpenDatabasesThroughDatabaseFileAlone()
        {
            Assert.Equal([nameof(DatabaseFile) + "." + ConstructorInfo.ConstructorName], OpeningSites(typeof(DatabaseFile).Assembly));
            Assert.Equal(
                typeof(OpeningWays).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(way => $"{nameof(DatabaseOpeningTests)}+{nameof(OpeningWays)}.{way.Name}").Order(),
                OpeningSites(typeof(DatabaseOpeningTests).Assembly).Order());
        }

        /// <summary>The methods of <paramref name="assembly"/> whose code makes a SQLite connection, or has something make one, of its own.</summary>
        private static IReadOnlyList<string> OpeningSites(Assembly assembly)
        {
            using PEReader file = new PEReader(File.OpenRead(assembly.Location));
            MetadataReader metadata = file.GetMetadataReader();
            HashSet<int> opening = metadata.MemberReferences.Where(member => Opens(metadata, metadata.GetMemberReference(member))).Select(member => MetadataTokens.GetToken(member)).ToHashSet();

            List<string> sites = new List<string>();
            foreach (MethodDefinition method in metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Where(method => method.RelativeVirtualAddress != 0))
            {
                byte[] code = file.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
                if (Enumerable.Range(0, Math.Max(0, code.Length - 3)).Any(at => opening.Contains(BitConverter.ToInt32(code, at))))
                {
                    sites.Add($"{NameOf(metadata, method.GetDeclaringType())}.{metadata.GetString(method.Name)}");
                }
            }

            return sites;
        }

        private static bool Opens(MetadataReader metadata, MemberReference member)
        {
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                return false;
            }

            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            string typeNamespace = metadata.GetString(type.Namespace);
            string typeName = metadata.GetString(type.Name);
            if (typeNamespace == typeof(SqliteConnection).Namespace)
            {
                return typeName == nameof(SqliteFactory) || (typeName == nameof(SqliteConnection) && metadata.GetString(member.Name) == ConstructorInfo.ConstructorName);
            }

            if (typeName == nameof(SqliteDataSourceFactory))
            {
                return true;
            }

            // DbUp opens a connection string itself, and is given a connection otherwise.
            bool lastParameterIsText = metadata.GetBlobBytes(member.Signature)[^1] == (byte)SignatureTypeCode.String;
            return (typeNamespace.StartsWith(typeof(SqliteConnectionManager).Namespace!, StringComparison.Ordinal) || typeName == nameof(SqliteExtensions)) && lastParameterIsText;
        }

        private static string NameOf(MetadataReader metadata, TypeDefinitionHandle handle)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            string name = metadata.GetString(type.Name);
            return type.GetDeclaringType().IsNil ? name : $"{NameOf(metadata, type.GetDeclaringType())}+{name}";
        }

        /// <summary>Never run: the ways to open a database the check knows, one to a method.</summary>
        private static class OpeningWays
        {
            internal static object Connection() => new SqliteConnection("Data Source=never");

            internal static object ConnectionWithoutAString() => new SqliteConnection();

            internal static object Factory() => SqliteFactory.Instance;

            internal static object LibraryFactory() => new SqliteDataSourceFactory();

            internal static object DbUp() => DeployChanges.To.SqliteDatabase("Data Source=never");

            internal static object DbUpConnections() => new SqliteConnectionManager("Data Source=never");
        }
    }
}
