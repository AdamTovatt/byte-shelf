using System.Data.Common;
using EasyReasy.Database.Sqlite;

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
    }
}
