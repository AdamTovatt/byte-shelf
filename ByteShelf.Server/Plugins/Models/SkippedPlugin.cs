namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A folder whose plugin was not loaded. The id, name and version are null where its <c>plugin.json</c> does not say.</summary>
    public sealed record SkippedPlugin
    {
        /// <summary>Its folder's full path, links resolved.</summary>
        public required string Folder { get; init; }

        public required string? Id { get; init; }

        public required string? Name { get; init; }

        public required string? Version { get; init; }

        /// <summary>Why, as a sentence.</summary>
        public required string Reason { get; init; }
    }
}
