using ByteShelf.Server.Backup.Models;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Backup.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --backup &lt;zip&gt;</c>: writes a backup of the root at <c>BYTESHELF_ROOT</c>
    /// beside the server running on it, and answers the process's exit code for a scheduler to act on.
    /// </summary>
    public static class BackupCommand
    {
        public const int Written = 0;

        /// <summary>Nothing was written: the root has no database, an object is damaged, the disk failed.</summary>
        public const int Failed = 1;

        /// <summary>Nothing was tried: the zip's path is not given, or <c>BYTESHELF_ROOT</c> is not set.</summary>
        public const int Usage = 2;

        public static int Run(string? rootPath, string? zipPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || zipPath.StartsWith("--", StringComparison.Ordinal))
            {
                logger.LogError("Usage: BYTESHELF_ROOT=<root> ByteShelf.Server --backup <path of the zip to write>");
                return Usage;
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                logger.LogError("Set BYTESHELF_ROOT to the root to back up.");
                return Usage;
            }

            try
            {
                RootFolder root = RootFolder.OpenExisting(rootPath);
                BackupSummary summary = RootBackup.WriteAsync(root, zipPath, CancellationToken.None).GetAwaiter().GetResult();
                logger.LogInformation(
                    "Backup written to {Path}: {Objects} objects, {Bytes} bytes; {Purged} files purged meanwhile were left out.",
                    Path.GetFullPath(zipPath),
                    summary.ObjectCount,
                    summary.ObjectBytes,
                    summary.FilesPurgedMeanwhile);
                return Written;
            }
            catch (Exception exception)
            {
                // Every failure, foreseen or not, is the one exit code a scheduler checks for.
                logger.LogError(exception, "Backup failed: {Message}", exception.Message);
                return Failed;
            }
        }
    }
}
