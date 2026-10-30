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

        /// <summary>
        /// The connection string for <see cref="DatabasePath"/>. Foreign keys are switched on here rather
        /// than by a pragma on one connection, because SQLite holds that setting per connection and a
        /// pooled connection that never ran the pragma would ignore every foreign key.
        /// </summary>
        public string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();

        /// <summary>
        /// Opens the root at <paramref name="path"/>, creating it and its folders when they are missing.
        /// </summary>
        public static RootFolder Open(string path)
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            RootFolder root = new RootFolder(fullPath);

            Directory.CreateDirectory(root.Path);
            Directory.CreateDirectory(root.ObjectsPath);
            Directory.CreateDirectory(root.StagingPath);
            Directory.CreateDirectory(root.KeysPath);

            return root;
        }
    }
}
