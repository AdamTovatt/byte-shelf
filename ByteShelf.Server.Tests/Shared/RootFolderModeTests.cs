using System.Runtime.Versioning;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>The file modes that keep a root's secrets, password hashes and files from other local users.</summary>
    [UnsupportedOSPlatform("windows")]
    public sealed class RootFolderModeTests : IAsyncLifetime
    {
        private TestRoot _testRoot = null!;

        private RootFolder Root => _testRoot.Root;

        public Task InitializeAsync()
        {
            _testRoot = TestRoot.Create();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _testRoot.DisposeAsync();
        }

        [UnixFact]
        public void Open_TheRootHoldingTheDatabase_IsOwnerOnly()
        {
            Assert.Equal(OwnerOnly.DirectoryPermissions, File.GetUnixFileMode(Root.Path));
            Assert.Equal(Root.Path, Path.GetDirectoryName(Root.DatabasePath));
            Assert.True(File.Exists(Root.DatabasePath));
        }

        [UnixFact]
        public void Open_ARootOthersCouldReach_IsMadeOwnerOnly()
        {
            File.SetUnixFileMode(Root.Path, OwnerOnly.DirectoryPermissions | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            RootFolder.Open(Root.Path);

            Assert.Equal(OwnerOnly.DirectoryPermissions, File.GetUnixFileMode(Root.Path));
        }

        [UnixFact]
        public void GetOrCreate_TheKeysFolderAndASecret_AreOwnerOnly()
        {
            RootKeyMaterial.GetOrCreate(Root, RootKeyMaterial.JwtSecretFileName);

            Assert.Equal(OwnerOnly.DirectoryPermissions, File.GetUnixFileMode(Root.KeysPath));
            Assert.Equal(OwnerOnly.FilePermissions, File.GetUnixFileMode(Path.Combine(Root.KeysPath, RootKeyMaterial.JwtSecretFileName)));
        }
    }
}
