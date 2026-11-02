using ByteShelf.Server.Shared.IO;
using Microsoft.Data.Sqlite;

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

        private RootFolder(string path)
        {
            Path = path;
            DatabasePath = System.IO.Path.Combine(path, DatabaseFileName);
            ObjectsPath = System.IO.Path.Combine(path, ObjectsFolderName);
            StagingPath = System.IO.Path.Combine(path, StagingFolderName);
            KeysPath = System.IO.Path.Combine(path, KeysFolderName);
        }

        public string Path { get; }

        public string DatabasePath { get; }

        /// <summary>File contents, stored whole under <c>objects/ab/cd/&lt;sha256&gt;</c> and never modified.</summary>
        public string ObjectsPath { get; }

        /// <summary>Uploads in progress. On the same file system as <see cref="ObjectsPath"/>, so finishing one is a rename.</summary>
        public string StagingPath { get; }

        public string KeysPath { get; }

        /// <summary>The connection string for <see cref="DatabasePath"/>.</summary>
        public string ConnectionString => ConnectionStringFor(DatabasePath);

        /// <summary>
        /// The connection string for a ByteShelf database at <paramref name="databasePath"/>. Foreign keys
        /// are switched on here rather than by a pragma on one connection, because SQLite holds that
        /// setting per connection and a pooled connection that never ran the pragma would ignore every
        /// foreign key. Without <paramref name="pooling"/>, closing the last connection releases the file,
        /// for a copy that is moved or deleted next.
        /// </summary>
        public static string ConnectionStringFor(string databasePath, bool pooling = true)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true,
                Pooling = pooling,
                DefaultTimeout = 30,
            }.ToString();
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
