namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>How soon a plugin is to do something, the soonest last.</summary>
    internal enum PluginPriority
    {
        /// <summary>Over contents it has yet to do it over, whenever they were stored.</summary>
        Backlog,

        /// <summary>Over contents just stored.</summary>
        Stored,

        /// <summary>Somebody waits for it.</summary>
        Asked,
    }
}
