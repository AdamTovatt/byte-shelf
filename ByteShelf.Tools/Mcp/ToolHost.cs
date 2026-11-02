namespace ByteShelf.Tools.Mcp
{
    /// <summary>Where the tools run, which decides whether a local path means anything to them, and whether they make image previews.</summary>
    internal sealed class ToolHost
    {
        private ToolHost(bool reachesLocalFiles, IImagePreviews? previews)
        {
            ReachesLocalFiles = reachesLocalFiles;
            Previews = previews;
        }

        /// <summary><c>byteshelf --mcp</c>, on the agent's own machine.</summary>
        public static ToolHost Local { get; } = new ToolHost(reachesLocalFiles: true, previews: null);

        /// <summary>The server's <c>/mcp</c>: its files are not the agent's, so it previews an image it cannot hand over whole.</summary>
        public static ToolHost Server(IImagePreviews previews)
        {
            return new ToolHost(reachesLocalFiles: false, previews);
        }

        /// <summary>Whether a local path the agent gives names a file the tools can read or write.</summary>
        public bool ReachesLocalFiles { get; }

        /// <summary>What makes image previews; null where the tools make none.</summary>
        public IImagePreviews? Previews { get; }
    }
}
