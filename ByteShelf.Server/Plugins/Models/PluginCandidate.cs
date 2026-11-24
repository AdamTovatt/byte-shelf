namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A folder that should hold a plugin, as far as its <c>plugin.json</c> says: a <see cref="ReadablePlugin"/> or an <see cref="UnreadablePlugin"/>.</summary>
    public abstract record PluginCandidate
    {
        private protected PluginCandidate()
        {
        }

        /// <summary>The folder's full path, with every link on the way resolved, so a link changed later is not followed.</summary>
        public abstract string Folder { get; }

        /// <summary>The plugin's id, null when the file names no valid one.</summary>
        public abstract string? Id { get; }

        /// <summary>The plugin as one skipped for <paramref name="reason"/>.</summary>
        public abstract SkippedPlugin SkippedFor(string reason);
    }

    /// <summary>A folder whose <c>plugin.json</c> is valid and for this server.</summary>
    public sealed record ReadablePlugin(string FolderPath, PluginManifest Manifest) : PluginCandidate
    {
        public override string Folder => FolderPath;

        public override string? Id => Manifest.Id;

        public override SkippedPlugin SkippedFor(string reason)
        {
            return new SkippedPlugin { Folder = Folder, Id = Manifest.Id, Name = Manifest.Name, Version = Manifest.Version, Reason = reason };
        }
    }

    /// <summary>A folder whose <c>plugin.json</c> is missing, malformed or for another server.</summary>
    /// <param name="Skipped">Why, with what the file says the plugin is all the same.</param>
    public sealed record UnreadablePlugin(SkippedPlugin Skipped) : PluginCandidate
    {
        public override string Folder => Skipped.Folder;

        public override string? Id => Skipped.Id;

        public override SkippedPlugin SkippedFor(string reason)
        {
            return Skipped with { Reason = reason };
        }
    }
}
