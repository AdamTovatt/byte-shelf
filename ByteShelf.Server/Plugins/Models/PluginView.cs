namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A loaded plugin, as the web UI needs it.</summary>
    public sealed class PluginView
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Version { get; init; }

        /// <summary>The address of its web UI module; null when it has no part in the web UI.</summary>
        public required string? ModuleUrl { get; init; }

        /// <summary>The address of its stylesheet, if it has one.</summary>
        public required string? StylesheetUrl { get; init; }

        /// <summary>Why its reader of files' contents could not run when it last had something to read, as a sentence; null when it is run or there is none, and for anyone but an admin.</summary>
        public required string? ReaderProblem { get; init; }

        /// <summary>Why its maker of derived files could not run when it last had something to make, as a sentence; null when it is run or there is none, and for anyone but an admin.</summary>
        public required string? MakerProblem { get; init; }
    }
}
