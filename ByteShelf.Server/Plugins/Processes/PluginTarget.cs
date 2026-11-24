namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>A plugin as its process is started for it.</summary>
    /// <param name="Id">Its id.</param>
    /// <param name="Location">Where its process finds it to load it.</param>
    public sealed record PluginTarget(string Id, string Location);
}
