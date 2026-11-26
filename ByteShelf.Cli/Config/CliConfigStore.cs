using System.Text.Json;
using ByteShelf.Tools;

namespace ByteShelf.Cli.Config
{
    /// <summary>
    /// The config file and its folder, readable only by their owner since the file holds a key or a
    /// session. Writes replace the whole file at once, so a reader never sees half of one.
    /// </summary>
    internal sealed class CliConfigStore
    {
        /// <summary>Names another config file, for a second server or an isolated test.</summary>
        public const string PathVariable = "BYTESHELF_CONFIG";

        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

        // Giving up after the timeout is not tested: it would take a timeout setting that only a test uses.
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(2);

        private readonly string? _filePath;

        /// <param name="path">The config file, relative to the current folder when not absolute; null when there is none to use.</param>
        public CliConfigStore(string? path)
        {
            _filePath = path == null ? null : Path.GetFullPath(path);
        }

        /// <exception cref="UsageException">There is no config file to use.</exception>
        public string FilePath => _filePath ?? throw new UsageException(
            $"No config folder was found to keep the login in. Name a config file with {PathVariable}.");

        /// <summary>
        /// The file <see cref="PathVariable"/> names, else <c>byteshelf/config.json</c> in the user's
        /// config folder: on Linux and macOS <c>$XDG_CONFIG_HOME</c> when it is an absolute path, else
        /// <c>.config</c> in the home folder, which is <c>$HOME</c> when that is absolute, else the account's
        /// home as the process finds it; <c>%APPDATA%</c> on Windows. The account's home and <c>%APPDATA%</c>
        /// are the process's own, not read from <paramref name="environment"/>. The folder is always absolute
        /// and need not exist yet; when none can be found, the store has no file and refuses only once a
        /// command reads or writes the config.
        /// </summary>
        public static CliConfigStore FromEnvironment(Func<string, string?> environment)
        {
            string? path = environment(PathVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                string? folder = ConfigFolder(environment);
                path = folder == null ? null : Path.Combine(folder, "byteshelf", "config.json");
            }

            return new CliConfigStore(path);
        }

        /// <summary>The user's config folder, or null when nothing names an absolute one.</summary>
        private static string? ConfigFolder(Func<string, string?> environment)
        {
            if (OperatingSystem.IsWindows())
            {
                return Absolute(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
            }

            string? configHome = Absolute(environment("XDG_CONFIG_HOME"));
            if (configHome != null)
            {
                return configHome;
            }

            // .NET asks the passwd database when the process has no HOME, which keeps a service or cron
            // job working; it answers a relative HOME as it is, so that finds no folder.
            string? home = Absolute(environment("HOME"))
                ?? Absolute(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));
            return home == null ? null : Path.Combine(home, ".config");
        }

        private static string? Absolute(string? folder)
        {
            return folder != null && Path.IsPathFullyQualified(folder) ? folder : null;
        }

        /// <summary>The config, or null when there is no file.</summary>
        /// <exception cref="UsageException">The file is not a config.</exception>
        public CliConfig? Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            CliConfig? config;
            try
            {
                // Shared for writing and deleting, so reading never stops another command from
                // replacing or removing the file, which Windows would otherwise refuse.
                using FileStream file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                config = JsonSerializer.Deserialize(file, CliJsonContext.Default.CliConfig);
            }
            catch (FileNotFoundException)
            {
                // Removed by a logout since the check above.
                return null;
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // NotSupportedException: a login without a kind, which names no type to read it as.
                throw Damaged(exception.Message, exception);
            }

            if (config != null
                && (!Uri.TryCreate(config.Server, UriKind.Absolute, out Uri? server) || (server.Scheme != Uri.UriSchemeHttps && server.Scheme != Uri.UriSchemeHttp)))
            {
                throw Damaged($"'{config.Server}' is not a server address", innerException: null);
            }

            return config;
        }

        public void Save(CliConfig config)
        {
            EnsureFolder();

            // Unique, so two writers can never interleave in one temporary file.
            string temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
            FileStreamOptions options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            try
            {
                using (FileStream file = new FileStream(temporary, options))
                {
                    JsonSerializer.Serialize(file, config, CliJsonContext.Default.CliConfig);
                }

                MoveIntoPlace(temporary);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
        }

        public void Delete()
        {
            File.Delete(FilePath);
        }

        /// <summary>
        /// Holds the config for one command at a time, across processes, while it reads, renews and
        /// writes a session: a refresh token works once, and two commands renewing with the same one
        /// would end the session.
        /// </summary>
        /// <exception cref="TimeoutException">Another command held it for <see cref="LockTimeout"/>.</exception>
        public async Task<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
        {
            EnsureFolder();
            DateTime giveUpAt = DateTime.UtcNow + LockTimeout;
            while (true)
            {
                try
                {
                    // The lock file stays: deleting it on release would let a waiter lock the deleted
                    // file while a third command locks a new one.
                    return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < giveUpAt)
                {
                    await Task.Delay(LockRetryDelay, cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new TimeoutException($"Another byteshelf command has held {FilePath} for {LockTimeout.TotalSeconds:0} seconds.", exception);
                }
            }
        }

        /// <summary>
        /// Replaces the config with <paramref name="temporary"/>. Windows refuses while another program
        /// holds the config open without sharing its deletion (an older byteshelf, a virus scanner), so
        /// the move is tried again for <see cref="MoveTimeout"/>: losing a renewed session's tokens here
        /// would end the session.
        /// </summary>
        /// <remarks>Not tested: Linux and macOS replace a file that is open, so nothing there refuses.</remarks>
        private void MoveIntoPlace(string temporary)
        {
            DateTime giveUpAt = DateTime.UtcNow + MoveTimeout;
            while (true)
            {
                try
                {
                    File.Move(temporary, FilePath, overwrite: true);
                    return;
                }
                catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && DateTime.UtcNow < giveUpAt)
                {
                    Thread.Sleep(LockRetryDelay);
                }
            }
        }

        private UsageException Damaged(string why, Exception? innerException)
        {
            string message = $"{FilePath} is not a byteshelf config ({why}). Fix it, or remove it with 'byteshelf logout' and sign in again.";
            return innerException == null ? new UsageException(message) : new UsageException(message, innerException);
        }

        /// <exception cref="UsageException">The folder cannot be made, as under a home folder of <c>/</c> for an account with no home.</exception>
        private void EnsureFolder()
        {
            string folder = Path.GetDirectoryName(FilePath)!;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(folder);
                }
                else
                {
                    Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new UsageException($"Cannot make the config folder {folder} ({exception.Message}). Name a config file elsewhere with {PathVariable}.", exception);
            }
        }
    }
}
