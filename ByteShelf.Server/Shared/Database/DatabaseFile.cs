using System.Data;
using System.Data.Common;
using EasyReasy.Database.Sqlite;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// A ByteShelf database on disk, and the one way to open it, which <c>DatabaseOpeningTests</c> keeps so:
    /// SQLite holds some settings per connection, and a connection opened without them ignores them. They are
    /// foreign keys, and overwriting what is deleted (<c>secure_delete</c>, see Metadata/Metadata.md), which
    /// no connection string can ask for.
    /// </summary>
    public sealed class DatabaseFile
    {
        // Makes the connections, and holds nothing: SQLite's pools are the process's.
        private readonly DbDataSource _connections;

        static DatabaseFile()
        {
            // What the repositories rely on, whichever connection they get.
            DatabaseTypeHandlers.RegisterAll();
        }

        /// <param name="pooling">Without it, closing the last connection releases the file, for a copy that is moved or deleted next.</param>
        public DatabaseFile(string path, bool pooling = true)
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = true,
                Pooling = pooling,
                DefaultTimeout = 30,
            }.ToString();
            _connections = new SqliteDataSourceFactory().CreateDataSource(connectionString);
        }

        public DbDataSource CreateDataSource()
        {
            return new DataSource(this);
        }

        /// <summary>An open connection of its own, beside the server's data source.</summary>
        public SqliteConnection OpenConnection()
        {
            SqliteConnection connection = CreateConnection();
            connection.Open();
            return connection;
        }

        /// <inheritdoc cref="OpenConnection"/>
        public async Task<SqliteConnection> OpenConnectionAsync()
        {
            SqliteConnection connection = CreateConnection();
            await connection.OpenAsync();
            return connection;
        }

        /// <summary>
        /// Closes this process's pooled connections to the database, which outlive a data source's service
        /// provider. When no other process has it open, SQLite then removes its <c>-wal</c> and <c>-shm</c>
        /// files. For a command run beside the server: once its work is done rather than whenever it exits,
        /// and before it deletes a database it made.
        /// </summary>
        public void Release()
        {
            using SqliteConnection connection = CreateConnection();
            SqliteConnection.ClearPool(connection);
        }

        private SqliteConnection CreateConnection()
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

        private sealed class DataSource : DbDataSource
        {
            private readonly DatabaseFile _database;

            public DataSource(DatabaseFile database)
            {
                _database = database;
            }

            public override string ConnectionString => _database._connections.ConnectionString;

            protected override DbConnection CreateDbConnection()
            {
                return _database.CreateConnection();
            }
        }
    }
}
