namespace ByteShelf.Tools.Mcp
{
    /// <summary>Where the tools run, which decides whether a local path means anything to them.</summary>
    internal sealed class ToolHost
    {
        private ToolHost(bool reachesLocalFiles)
        {
            ReachesLocalFiles = reachesLocalFiles;
        }

        /// <summary><c>byteshelf --mcp</c>, on the agent's own machine.</summary>
        public static ToolHost Local { get; } = new ToolHost(reachesLocalFiles: true);

        /// <summary>The server's <c>/mcp</c>: its files are not the agent's.</summary>
        public static ToolHost Server { get; } = new ToolHost(reachesLocalFiles: false);

        /// <summary>Whether a local path the agent gives names a file the tools can read or write.</summary>
        public bool ReachesLocalFiles { get; }
    }
}
