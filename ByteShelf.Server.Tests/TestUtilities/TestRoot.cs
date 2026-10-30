using System.Data.Common;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Root;
using EasyReasy.Database;
using EasyReasy.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A root folder of a test's own, with a database migrated by the production
    /// <see cref="DatabaseMigrator"/>. The migrations run once per test run into a template that each
    /// root copies, so a fresh database costs a file copy. Roots live under the test output folder and
    /// are deleted on dispose.
    /// </summary>
    public sealed class TestRoot : IAsyncDisposable
    {
        private static readonly string RootsPath = Path.Combine(AppContext.BaseDirectory, "test-roots");
        private static readonly Lazy<string> TemplateDatabasePath = new Lazy<string>(CreateTemplateDatabase);

        private TestRoot(RootFolder root)
        {
            Root = root;
            DataSource = new SqliteDataSourceFactory().CreateDataSource(root.ConnectionString);
            SessionFactory = new DbSessionFactory(DataSource);
        }

        public RootFolder Root { get; }

        public DbDataSource DataSource { get; }

        public IDbSessionFactory SessionFactory { get; }

        public static TestRoot Create()
        {
            string templatePath = TemplateDatabasePath.Value;
            RootFolder root = RootFolder.Open(Path.Combine(RootsPath, Guid.NewGuid().ToString("N")));
            File.Copy(templatePath, root.DatabasePath);
            return new TestRoot(root);
        }

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(Root.Path))
            {
                Directory.Delete(Root.Path, recursive: true);
            }
        }

        private static string CreateTemplateDatabase()
        {
            DatabaseTypeHandlers.RegisterAll();

            // One template per run; earlier runs' templates are left over only because nothing deletes
            // a template while the run still copies it.
            if (Directory.Exists(RootsPath))
            {
                foreach (string previous in Directory.EnumerateDirectories(RootsPath, "template-*"))
                {
                    Directory.Delete(previous, recursive: true);
                }
            }

            RootFolder template = RootFolder.Open(Path.Combine(RootsPath, "template-" + Guid.NewGuid().ToString("N")));
            DatabaseMigrator.RunMigrations(template.ConnectionString, NullLogger.Instance);

            // Fold the write-ahead log into the file, so copying the file alone copies everything.
            using (SqliteConnection connection = new SqliteConnection(template.ConnectionString))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            return template.DatabasePath;
        }
    }
}
