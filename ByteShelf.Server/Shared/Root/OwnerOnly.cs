namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// Files and folders readable by their owner only, for anything holding secrets or password hashes.
    /// Windows keeps its own access lists, so there they are left as created.
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
