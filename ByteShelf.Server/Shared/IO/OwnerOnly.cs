namespace ByteShelf.Server.Shared.IO
{
    /// <summary>
    /// Files and folders readable by their owner only, for anything holding secrets, password hashes or
    /// reset links. Windows keeps its own access lists, so there they are left as created.
    /// </summary>
    public static class OwnerOnly
    {
        public const UnixFileMode FilePermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        public const UnixFileMode DirectoryPermissions = FilePermissions | UnixFileMode.UserExecute;

        /// <summary>Options for writing a file that is created readable by its owner only.</summary>
        public static FileStreamOptions Write(FileMode mode)
        {
            FileStreamOptions options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = FilePermissions;
            }

            return options;
        }

        /// <summary>
        /// Creates the folder at <paramref name="path"/>, with its missing parents, reachable by its owner
        /// only. A folder that exists already is left as it is, permissions included.
        /// </summary>
        public static void CreateDirectory(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(path, DirectoryPermissions);
            }
        }

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/>, which must not exist yet, readable by
        /// its owner only: to a temporary sibling first, renamed into place in one step, so a crash or a
        /// reader never meets part of it. A sibling rather than a temp folder, so the rename stays within
        /// one filesystem, where it is atomic. The sibling is removed when the rename does not happen.
        /// </summary>
        /// <exception cref="IOException"><paramref name="path"/> exists, or the disk failed.</exception>
        public static void WriteThenRename(string path, string content)
        {
            string temporaryPath = path + ".tmp";
            try
            {
                // Create, not CreateNew: a sibling a crash left is written over.
                using (StreamWriter writer = new StreamWriter(temporaryPath, Write(FileMode.Create)))
                {
                    writer.Write(content);
                }

                File.Move(temporaryPath, path, overwrite: false);
            }
            finally
            {
                // A file is left to delete only when the rename did not happen. A failure here is swallowed:
                // an exception from a finally block would replace the one in flight, which says why.
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>Makes the existing folder at <paramref name="path"/> reachable by its owner only.</summary>
        public static void RestrictDirectory(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, DirectoryPermissions);
            }
        }
    }
}
