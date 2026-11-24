using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>What a process leaves on disk, and how it is found by the contents it was given.</summary>
    public sealed class WorkFolderTests : IAsyncLifetime
    {
        private readonly TestRoot _root = TestRoot.Create();

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return _root.DisposeAsync().AsTask();
        }

        [Fact]
        public void Find_InARootWithNone_IsEmpty()
        {
            Assert.Empty(WorkFolder.Find(_root.Root));
        }

        [Fact]
        public void GetContents_AreThoseAdded()
        {
            WorkFolder folder = WorkFolder.Create(_root.Root);
            Assert.Empty(folder.GetContents());

            folder.Add("a");
            folder.Add("b");

            Assert.Equal(["a", "b"], Assert.Single(WorkFolder.Find(_root.Root)).GetContents().Order());
        }

        [Fact]
        public void Find_OneWhoseListAloneIsLeft_HasItsContentsUntilItIsRemoved()
        {
            WorkFolder folder = WorkFolder.Create(_root.Root);
            folder.Add("a");
            Directory.Delete(folder.Path);

            WorkFolder found = Assert.Single(WorkFolder.Find(_root.Root));
            Assert.Equal(["a"], found.GetContents());
            found.Remove();

            Assert.Empty(WorkFolder.Find(_root.Root));
        }

        [Fact]
        public void Empty_RemovesEverythingInItAndKeepsItAndItsList()
        {
            WorkFolder folder = WorkFolder.Create(_root.Root);
            folder.Add("a");
            Directory.CreateDirectory(Path.Combine(folder.Path, "inner", "innermost"));
            File.WriteAllText(Path.Combine(folder.Path, "inner", "innermost", "file"), "read from contents");
            File.WriteAllText(Path.Combine(folder.Path, "file"), "read from contents");

            folder.Empty();

            Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
            Assert.Equal(["a"], folder.GetContents());
        }

        [Fact]
        public void Remove_LeavesNothingOfItAndEveryOther()
        {
            WorkFolder other = WorkFolder.Create(_root.Root);
            WorkFolder folder = WorkFolder.Create(_root.Root);
            folder.Add("a");
            File.WriteAllText(Path.Combine(folder.Path, "file"), "read from contents");

            folder.Remove();

            Assert.Equal(other.Path, Assert.Single(WorkFolder.Find(_root.Root)).Path);
            Assert.Equal([other.Path], Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(folder.Path)!));
        }
    }
}
