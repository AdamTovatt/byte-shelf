namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// Files and folders readable by their owner only from the moment they exist, for anything holding
    /// secrets or password hashes. Windows keeps its own access lists, so there they are left as created.
    /// </summary>
    public static class OwnerOnly
    {
        public const UnixFileMode File = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        public const UnixFileMode Directory = File | UnixFileMode.UserExecute;

        /// <summary>Options for writing a file that is created readable by its owner only.</summary>
        public static FileStreamOptions Write(FileMode mode)
        {
            FileStreamOptions options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = File;
            }

            return options;
        }
    }
}
