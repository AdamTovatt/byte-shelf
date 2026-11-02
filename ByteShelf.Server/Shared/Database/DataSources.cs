using System.Data.Common;
using EasyReasy.Database.Sqlite;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>How the server opens a database: SQLite, with the type handlers its repositories rely on registered first.</summary>
    public static class DataSources
    {
        public static DbDataSource Create(string connectionString)
        {
            DatabaseTypeHandlers.RegisterAll();
            return new SqliteDataSourceFactory().CreateDataSource(connectionString);
        }

        /// <summary>
        /// Closes this process's pooled connections to the database behind <paramref name="connectionString"/>,
        /// which outlive a data source's service provider. When no other process has it open, SQLite then
        /// removes its <c>-wal</c> and <c>-shm</c> files; left open until the process exits, they would stay,
        /// owned by whoever ran it, and a server running as another user could not open its database.
        /// For a command run beside the server, before it exits.
        /// </summary>
        public static void Release(string connectionString)
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(connection);
        }
    }
}
