using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Middleware;

namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// The frame of a command the server runs on the root at <c>BYTESHELF_ROOT</c> instead of starting,
    /// from its own machine with no login, with the server running or stopped: its user and replication
    /// commands. It opens the root, refuses a database with migrations still to run, logs what the command
    /// did or why it was refused, and releases the database before exiting. Answers the exit code. A
    /// command that sets up a server before its first start may make the root first (see
    /// <see cref="RunAsync"/>).
    /// </summary>
    public static class RootCommand
    {
        public const int Done = 0;

        /// <summary>
        /// Refused or failed, as the API would refuse it, or no database at the root (for a command that
        /// does not make one). Nothing was changed: a root the command made is removed again.
        /// </summary>
        public const int Failed = 1;

        /// <summary>Nothing was tried: the arguments are wrong, or <c>BYTESHELF_ROOT</c> is not set.</summary>
        public const int Usage = 2;

        /// <summary>
        /// Runs <paramref name="apply"/> on the root at <paramref name="rootPath"/>, logging the line it
        /// answers. With <paramref name="startsARoot"/>, a root with no database yet is made and migrated
        /// first, as the server's first start would, for a command that sets up a server before that start
        /// (its first admin, or the primary it follows); it is removed again when the command does not
        /// finish. Any other command refuses such a root, so a mistyped one is not made.
        /// </summary>
        public static async Task<int> RunAsync(string? rootPath, ILogger logger, bool startsARoot, Func<RootFolder, Task<string>> apply)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                logger.LogError("Set BYTESHELF_ROOT to the server's root.");
                return Usage;
            }

            NewRoot? made = null;
            int result;
            try
            {
                RootFolder root;
                if (startsARoot && !RootFolder.HasDatabase(rootPath))
                {
                    made = NewRoot.Before(rootPath);
                    root = DatabaseMigrator.MigrateRoot(rootPath, logger);
                }
                else
                {
                    root = RootFolder.OpenExisting(rootPath);
                }

                result = await RunOnAsync(root, logger, apply);
            }
            catch (Exception exception) when (exception is FileNotFoundException || ExceptionHandlingMiddleware.RefusalMessage(exception) != null)
            {
                // What the person running it can act on, as the API would answer it; a root without a
                // database besides.
                logger.LogError("{Message}", ExceptionHandlingMiddleware.RefusalMessage(exception) ?? exception.Message);
                result = Failed;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed: {Message}", exception.Message);
                result = Failed;
            }

            if (made != null)
            {
                if (result == Done)
                {
                    // Whoever ran it owns the root it made, and the server can open no root but its own user's.
                    // Said rather than refused for root: a server in a container commonly runs as root.
                    logger.LogInformation("Made the root {Path}, owned by {User}: run the server as {User}.", made.Path, Environment.UserName, Environment.UserName);
                }
                else
                {
                    made.Remove();
                }
            }

            return result;
        }

        private static async Task<int> RunOnAsync(RootFolder root, ILogger logger, Func<RootFolder, Task<string>> apply)
        {
            try
            {
                // Not migrated here: the server may be running on this database, and an executable newer
                // than the server would upgrade it under it.
                if (!DatabaseMigrator.IsUpToDate(root.ConnectionString))
                {
                    logger.LogError("The database is from an older version of the server: start this version on it, or run ByteShelf.Server --migrate-only, first.");
                    return Failed;
                }

                logger.LogInformation("{Done}", await apply(root));
                return Done;
            }
            finally
            {
                // Refusing anyone but the root's owner would need the folder's owner, which .NET does not
                // expose; releasing the connections leaves no files owned by whoever ran this instead.
                DataSources.Release(root.ConnectionString);
            }
        }

        /// <summary>
        /// What of a root was missing before a command made it, so a command that does not finish removes
        /// what it made and nothing else: the whole folder when it was missing, else the database and the
        /// folders inside that were missing, keeping an empty folder made for the server beforehand.
        /// </summary>
        private sealed class NewRoot
        {
            private readonly bool _folderWasThere;
            private readonly IReadOnlyList<string> _madeFolders;

            private NewRoot(string path, bool folderWasThere, IReadOnlyList<string> madeFolders)
            {
                Path = path;
                _folderWasThere = folderWasThere;
                _madeFolders = madeFolders;
            }

            public string Path { get; }

            public static NewRoot Before(string rootPath)
            {
                string path = System.IO.Path.GetFullPath(rootPath);
                List<string> missing = new[] { RootFolder.ObjectsFolderName, RootFolder.StagingFolderName, RootFolder.KeysFolderName }
                    .Select(name => System.IO.Path.Combine(path, name))
                    .Where(folder => !Directory.Exists(folder))
                    .ToList();
                return new NewRoot(path, Directory.Exists(path), missing);
            }

            public void Remove()
            {
                string databasePath = System.IO.Path.Combine(Path, RootFolder.DatabaseFileName);
                DataSources.Release(RootFolder.ConnectionStringFor(databasePath));
                if (!_folderWasThere)
                {
                    Directory.Delete(Path, recursive: true);
                    return;
                }

                foreach (string file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                {
                    File.Delete(file);
                }

                foreach (string folder in _madeFolders)
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
    }
}
