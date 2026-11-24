using System.Text.Json;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>How a run of a plugin over some contents ended.</summary>
    internal abstract record PluginRunResult
    {
        private PluginRunResult()
        {
        }

        internal sealed record Answered(JsonElement Answer) : PluginRunResult;

        /// <param name="Reason">Why, without anything read from the contents.</param>
        internal sealed record Failed(string Reason) : PluginRunResult;

        /// <summary>The contents could not be opened or read: removed meanwhile, or the disk failed.</summary>
        internal sealed record ContentsNotRead : PluginRunResult;
    }
}
