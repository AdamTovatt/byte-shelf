using System.Text;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// The folder a plugin's process works in, under <c>derived/work/</c>: everything it leaves on disk is in it.
    /// Beside it is a list of the contents the process was given, so that one that could not be removed is found
    /// by them, whichever server left it.
    /// </summary>
    /// <remarks>
    /// It is in the root so that what is made in it is moved, not copied, to where it is kept. That tells a plugin
    /// where the root is, which hides from none: its process runs as the server's user and may open what that may.
    /// </remarks>
    internal sealed class WorkFolder
    {
        internal const string ContentsSuffix = ".contents";

        private const string WorkFolderName = "work";

        private WorkFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        private string ContentsPath => Path + ContentsSuffix;

        /// <summary>A new one in <paramref name="root"/>.</summary>
        public static WorkFolder Create(RootFolder root)
        {
            WorkFolder folder = new WorkFolder(System.IO.Path.Combine(AllIn(root), Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder.Path);
            return folder;
        }

        /// <summary>Every one in <paramref name="root"/>.</summary>
        public static IReadOnlyList<WorkFolder> Find(RootFolder root)
        {
            return Directory.Exists(AllIn(root))
                ? Directory.EnumerateFileSystemEntries(AllIn(root))
                    .Select(path => path.EndsWith(ContentsSuffix, StringComparison.Ordinal) ? path[..^ContentsSuffix.Length] : path)
                    .Distinct(StringComparer.Ordinal)
                    .Select(path => new WorkFolder(path))
                    .ToList()
                : [];
        }

        /// <summary>Notes that the process is given the contents, before it is.</summary>
        public void Add(string sha256)
        {
            using FileStream contents = new FileStream(ContentsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            contents.Write(Encoding.ASCII.GetBytes(sha256 + "\n"));
        }

        /// <summary>The contents the process was given.</summary>
        public IReadOnlySet<string> GetContents()
        {
            try
            {
                using FileStream contents = new FileStream(ContentsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new StreamReader(contents);
                return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return new HashSet<string>();
            }
        }

        /// <summary>Removes everything in it.</summary>
        /// <exception cref="IOException">Something could not be removed.</exception>
        /// <exception cref="UnauthorizedAccessException">Something could not be removed.</exception>
        public void Empty()
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(Path).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo { LinkTarget: null } folder)
                {
                    folder.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }
        }

        /// <exception cref="IOException">It could not be removed.</exception>
        /// <exception cref="UnauthorizedAccessException">It could not be removed.</exception>
        public void Remove()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            // Last: while anything is left, so is what it may be of.
            File.Delete(ContentsPath);
        }

        private static string AllIn(RootFolder root)
        {
            return System.IO.Path.Combine(root.DerivedPath, WorkFolderName);
        }
    }
}
