namespace ByteShelf.Server.Shared.IO
{
    /// <summary>What the framework does not say about a path.</summary>
    public static class FilePaths
    {
        /// <summary>
        /// Whether <paramref name="path"/> is <paramref name="folder"/> or inside it. Compares the paths as
        /// written, without resolving links.
        /// </summary>
        public static bool IsInside(string path, string folder)
        {
            string relative = Path.GetRelativePath(folder, path);
            return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }

        /// <summary>
        /// The full path of the folder at <paramref name="path"/> with the links in every part of it
        /// resolved. One that is not there, or whose links lead in a circle, is answered as written.
        /// </summary>
        public static string ResolveFolder(string path)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

            // Only a folder the system itself got to, so following its links ends.
            return Directory.Exists(full) ? ResolveExisting(full) : full;
        }

        private static string ResolveExisting(string path)
        {
            string? parent = Path.GetDirectoryName(path);
            if (parent == null)
            {
                return path;
            }

            string resolved = Path.Combine(ResolveExisting(parent), Path.GetFileName(path));
            FileSystemInfo? target = new DirectoryInfo(resolved).ResolveLinkTarget(returnFinalTarget: false);
            return target == null ? resolved : ResolveExisting(Path.TrimEndingDirectorySeparator(target.FullName));
        }
    }
}
