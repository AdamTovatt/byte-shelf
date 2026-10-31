using System.Text.Json;

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

        public CliConfigStore(string path)
        {
            FilePath = Path.GetFullPath(path);
        }

        public string FilePath { get; }

        /// <summary>
        /// The file <see cref="PathVariable"/> names, else <c>byteshelf/config.json</c> in the user's
        /// config folder: <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on Linux and macOS, <c>%APPDATA%</c> on Windows.
        /// </summary>
        public static CliConfigStore FromEnvironment(Func<string, string?> environment)
        {
            string? path = environment(PathVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "byteshelf", "config.json");
            }

            return new CliConfigStore(path);
        }

        /// <summary>The config, or null when there is no file.</summary>
        /// <exception cref="UsageException">The file is not a config.</exception>
        public CliConfig? Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            try
            {
                using FileStream file = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize(file, CliJsonContext.Default.CliConfig);
            }
            catch (JsonException exception)
            {
                throw new UsageException($"{FilePath} is not a byteshelf config ({exception.Message}). Fix it, or remove it with 'byteshelf logout' and sign in again.", exception);
            }
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

                File.Move(temporary, FilePath, overwrite: true);
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

        private void EnsureFolder()
        {
            string folder = Path.GetDirectoryName(FilePath)!;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}
