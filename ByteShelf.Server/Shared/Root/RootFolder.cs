using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.IO;

namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// The one folder that holds a whole ByteShelf instance: the SQLite database, every stored
    /// object, uploads in progress and the server's key material. Nothing the instance needs lives
    /// outside it, so a zip of a stopped server's root is a complete copy.
    /// </summary>
    public sealed class RootFolder
    {
        public const string DatabaseFileName = "byteshelf.db";
        public const string ObjectsFolderName = "objects";
        public const string StagingFolderName = "staging";
        public const string KeysFolderName = "keys";
        public const string DerivedFolderName = "derived";

        private RootFolder(string path)
        {
            Path = path;
            DatabasePath = System.IO.Path.Combine(path, DatabaseFileName);
            Database = new DatabaseFile(DatabasePath);
            ObjectsPath = System.IO.Path.Combine(path, ObjectsFolderName);
            StagingPath = System.IO.Path.Combine(path, StagingFolderName);
            KeysPath = System.IO.Path.Combine(path, KeysFolderName);
            DerivedPath = System.IO.Path.Combine(path, DerivedFolderName);
        }

        public string Path { get; }

        public string DatabasePath { get; }

        /// <summary>File contents, stored whole under <c>objects/ab/cd/&lt;sha256&gt;</c> and never modified.</summary>
        public string ObjectsPath { get; }

        /// <summary>Uploads in progress. On the same file system as <see cref="ObjectsPath"/>, so finishing one is a rename.</summary>
        public string StagingPath { get; }

        public string KeysPath { get; }

        /// <summary>The database at <see cref="DatabasePath"/>, opened through this alone.</summary>
        public DatabaseFile Database { get; }

        /// <summary>What plugins made from file contents: a cache, made again when it is gone, and in no backup.</summary>
        public string DerivedPath { get; }

        /// <summary>
        /// The root a server at <paramref name="path"/> would have, of which nothing is made or opened: for
        /// registering the server's services without running them, and for removing one.
        /// </summary>
        public static RootFolder At(string path)
        {
            return new RootFolder(System.IO.Path.GetFullPath(path));
        }

        /// <summary>
        /// Opens the root at <paramref name="path"/>, creating it and its folders when they are missing. The
        /// root is made reachable by its owner only, an existing one too, so the database, the stored
        /// objects and uploads in progress are private whatever the umask gave them.
        /// </summary>
        public static RootFolder Open(string path)
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            RootFolder root = new RootFolder(fullPath);

            Directory.CreateDirectory(root.Path);
            OwnerOnly.RestrictDirectory(root.Path);
            Directory.CreateDirectory(root.ObjectsPath);
            Directory.CreateDirectory(root.StagingPath);
            Directory.CreateDirectory(root.KeysPath);

            return root;
        }

        /// <summary>
        /// Opens the root of a server that has run at <paramref name="path"/>, creating nothing: for work
        /// beside the server running on it, such as a backup.
        /// </summary>
        /// <exception cref="FileNotFoundException">There is no ByteShelf database at <paramref name="path"/>.</exception>
        public static RootFolder OpenExisting(string path)
        {
            RootFolder root = new RootFolder(System.IO.Path.GetFullPath(path));
            if (!File.Exists(root.DatabasePath))
            {
                throw new FileNotFoundException($"There is no ByteShelf database in {root.Path}.", root.DatabasePath);
            }

            return root;
        }

        /// <summary>Whether a ByteShelf database is at <paramref name="path"/>, which a server's first start makes.</summary>
        public static bool HasDatabase(string path)
        {
            return File.Exists(new RootFolder(System.IO.Path.GetFullPath(path)).DatabasePath);
        }
    }
}
