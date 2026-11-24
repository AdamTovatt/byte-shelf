namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>The readers of the plugins this server loaded: the one place that says which there are, and how each is run.</summary>
    public interface IPluginReaders
    {
        /// <summary>Each plugin's, one at most, in the order they run.</summary>
        IReadOnlyList<PluginReader> All { get; }
    }
}
