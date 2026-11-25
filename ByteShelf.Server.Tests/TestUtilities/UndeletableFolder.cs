using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A folder a run over some contents left that the server cannot remove, on Unix, until it is released.</summary>
    public sealed class UndeletableFolder : IDisposable
    {
        private readonly string _inner;

        /// <summary>The one the <see cref="TestMaker"/>'s run over contents that say <c>undeletable</c> left.</summary>
        public UndeletableFolder(TestRoot root, string sha256)
            : this(Assert.Single(WorkFolder.Find(root.Root), folder => folder.GetContents().Contains(sha256)).Path)
        {
        }

        private UndeletableFolder(string path)
        {
            Path = path;
            _inner = System.IO.Path.Combine(path, "undeletable");
        }

        public string Path { get; }

        /// <summary>One left in <paramref name="root"/> by an earlier server.</summary>
        public static UndeletableFolder Create(RootFolder root, string sha256)
        {
            WorkFolder work = WorkFolder.Create(root);
            work.Add(sha256);
            UndeletableFolder folder = new UndeletableFolder(work.Path);
            Directory.CreateDirectory(folder._inner);
            File.WriteAllText(System.IO.Path.Combine(folder._inner, "file"), "read from contents");
            folder.SetWritable(false);
            return folder;
        }

        public void Release()
        {
            if (Directory.Exists(_inner))
            {
                SetWritable(true);
            }
        }

        public void Dispose()
        {
            Release();
        }

        private void SetWritable(bool writable)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_inner, UnixFileMode.UserRead | UnixFileMode.UserExecute | (writable ? UnixFileMode.UserWrite : UnixFileMode.None));
            }
        }
    }
}
