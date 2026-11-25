using System.Text.Json;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// One plugin's way of doing a <see cref="ProcessWork"/> over contents, in its process. It may leave files in
    /// <paramref name="folder"/>, the working folder and the one for temporary files, which the server empties once it
    /// has taken what it wants.
    /// </summary>
    /// <param name="arguments">What it needs to know beside the contents.</param>
    /// <param name="contents">The contents, a seekable stream at its start.</param>
    /// <param name="folder">Where it may write.</param>
    /// <returns>The answer; throwing is the contents' doing unless it throws the exceptions below.</returns>
    /// <exception cref="PluginUnavailableException">The plugin cannot do it, whatever the contents.</exception>
    internal delegate Task<JsonElement> PluginWork(JsonElement arguments, Stream contents, string folder);
}
