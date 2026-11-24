using System.Text.Json;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>What the server asks a plugin's process to do.</summary>
    /// <param name="Work">What, as the plugin's work names it (<see cref="PluginWork"/>).</param>
    /// <param name="Arguments">What it needs to know beside the contents.</param>
    /// <param name="Length">How many bytes the contents are.</param>
    internal sealed record RunRequest(string Work, JsonElement Arguments, long Length);
}
