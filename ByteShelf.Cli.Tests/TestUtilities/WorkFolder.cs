namespace ByteShelf.Cli.Tests.TestUtilities
{
    /// <summary>A folder of a test's own under the test output, deleted on dispose.</summary>
    internal sealed class WorkFolder : IDisposable
    {
        public WorkFolder()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "cli-work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
