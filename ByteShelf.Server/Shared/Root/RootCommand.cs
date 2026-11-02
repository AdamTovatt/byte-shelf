using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Middleware;

namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// The frame of a command the server runs on the root at <c>BYTESHELF_ROOT</c> instead of starting,
    /// from its own machine with no login, with the server running or stopped: its user and replication
    /// commands. It opens the root, refuses a database with migrations still to run, logs what the
    /// command did or why it was refused, and releases the database before exiting. Answers the exit code.
    /// </summary>
    public static class RootCommand
    {
        public const int Done = 0;

        /// <summary>Refused or failed, as the API would refuse it, or a root without a database; nothing was changed.</summary>
        public const int Failed = 1;

        /// <summary>Nothing was tried: the arguments are wrong, or <c>BYTESHELF_ROOT</c> is not set.</summary>
        public const int Usage = 2;

        /// <summary>Runs <paramref name="apply"/> on the root at <paramref name="rootPath"/>, logging the line it answers.</summary>
        public static async Task<int> RunAsync(string? rootPath, ILogger logger, Func<RootFolder, Task<string>> apply)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                logger.LogError("Set BYTESHELF_ROOT to the server's root.");
                return Usage;
            }

            try
            {
                RootFolder root = RootFolder.OpenExisting(rootPath);
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
            catch (Exception exception) when (exception is FileNotFoundException || ExceptionHandlingMiddleware.RefusalMessage(exception) != null)
            {
                // What the person running it can act on, as the API would answer it; a root without a
                // database besides.
                logger.LogError("{Message}", ExceptionHandlingMiddleware.RefusalMessage(exception) ?? exception.Message);
                return Failed;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed: {Message}", exception.Message);
                return Failed;
            }
        }
    }
}
