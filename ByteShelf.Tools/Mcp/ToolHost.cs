using ByteShelfCommon;

namespace ByteShelf.Tools.Mcp
{
    /// <summary>Where the tools run, which decides whether a local path means anything to them, and what they show of a file an agent cannot be shown itself.</summary>
    internal sealed class ToolHost
    {
        private ToolHost(bool reachesLocalFiles, IReadOnlyList<MediaTypePattern> previewedTypes)
        {
            ReachesLocalFiles = reachesLocalFiles;
            PreviewedTypes = previewedTypes;
        }

        /// <summary><c>byteshelf --mcp</c>, on the agent's own machine: the agent saves a file there and looks at it.</summary>
        public static ToolHost Local { get; } = new ToolHost(reachesLocalFiles: true, previewedTypes: []);

        /// <summary>
        /// The server's <c>/mcp</c>: its files are not the agent's, so a file it cannot hand over whole is shown as what a
        /// plugin makes of it for agents (<see cref="DerivedKind.Agent"/>).
        /// </summary>
        /// <param name="previewedTypes">The types of the files the server's plugins make it from; none without such a plugin.</param>
        public static ToolHost Server(IReadOnlyList<MediaTypePattern> previewedTypes)
        {
            return new ToolHost(reachesLocalFiles: false, previewedTypes);
        }

        /// <summary>Whether a local path the agent gives names a file the tools can read or write.</summary>
        public bool ReachesLocalFiles { get; }

        /// <summary>The types of the files shown as a preview when they cannot be answered as they are.</summary>
        public IReadOnlyList<MediaTypePattern> PreviewedTypes { get; }

        /// <summary>Whether a file of <paramref name="mediaType"/> is shown as a preview then.</summary>
        public bool ShowsPreviewOf(string mediaType)
        {
            return PreviewedTypes.Any(pattern => pattern.Includes(mediaType));
        }
    }
}
