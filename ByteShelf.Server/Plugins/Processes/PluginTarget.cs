namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// A plugin as its process is started for it. Public, unlike the rest of how processes run, as the plugins the
    /// seams of derived files and metadata answer (<c>IDerivedFilePlugins</c>, <c>IPluginReaders</c>) carry it.
    /// </summary>
    /// <param name="Id">Its id.</param>
    /// <param name="Location">Where its process finds it to load it.</param>
    public sealed record PluginTarget(string Id, string Location);
}
