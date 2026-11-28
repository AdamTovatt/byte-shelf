namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>A plugin's runs in one lane, which have a turn and a process of their own.</summary>
    internal sealed record PluginInLane(string PluginId, PluginLane Lane);
}
