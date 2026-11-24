using System.Data;
using System.Data.Common;
using EasyReasy.Database.Sqlite;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// A ByteShelf database on disk, and the one way to open it, which <c>DatabaseOpeningTests</c> keeps so:
    /// SQLite holds some settings per connection, and a connection opened without them ignores them. They are
    /// foreign keys, which the connection string asks for, and overwriting what is deleted
    /// (<c>secure_delete</c>, see Metadata/Metadata.md), which it cannot.
    /// </summary>
    public sealed class DatabaseFile : DbDataSource
    {
        private readonly string _path;

#pragma warning disable CA2213 // Holds nothing to dispose: SQLite's pools are the process's.
        private readonly DbDataSource _connections;
#pragma warning restore CA2213

        static DatabaseFile()
        {
            // What the repositories rely on, whichever connection they get.
            DatabaseTypeHandlers.RegisterAll();
        }

        /// <param name="pooling">Without it, closing the last connection releases the file, for a copy that is moved or deleted next.</param>
        public DatabaseFile(string path, bool pooling = true)
        {
            _path = path;
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = true,
                Pooling = pooling,
                DefaultTimeout = 30,
            }.ToString();
            _connections = new SqliteDataSourceFactory().CreateDataSource(connectionString);
        }

        public override string ConnectionString => _connections.ConnectionString;

        /// <summary>
        /// Closes this process's pooled connections to the database, which outlive the service provider.
        /// When no other process has it open, SQLite then removes its <c>-wal</c> and <c>-shm</c>
        /// files. For a command run beside the server: once its work is done rather than whenever it exits.
        /// </summary>
        public void Release()
        {
            using SqliteConnection connection = CreateDbConnection();
            SqliteConnection.ClearPool(connection);
        }

        /// <summary>Removes the database with every file SQLite keeps beside it, for what made it and is undoing that.</summary>
        public void Delete()
        {
            Release();
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(_path + suffix);
            }
        }

        // Set through an event because the library's data source makes the connection. If it could make a
        // connection type of ours, or had a hook for opening, the setting would belong there.
        protected override SqliteConnection CreateDbConnection()
        {
            SqliteConnection connection = (SqliteConnection)_connections.CreateConnection();

            // Raised by every way of opening, and each time a pooled connection is taken again.
            connection.StateChange += OverwriteDeletedOnceOpen;
            return connection;
        }

        private static void OverwriteDeletedOnceOpen(object sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open)
            {
                return;
            }

            SqliteConnection connection = (SqliteConnection)sender;
            try
            {
                // ON rather than FAST, which leaves free pages as they are.
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA secure_delete = ON";
                command.ExecuteNonQuery();
            }
            catch
            {
                // Never handed out open without it. Untested: nothing makes the pragma fail.
                connection.Close();
                throw;
            }
        }
    }
}
