using System.Collections.Concurrent;
using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// Which parts of plugins could not run when they last had something to do, and why: admins see it with the plugin.
    /// A reason holds nothing of the contents it was given.
    /// </summary>
    public sealed class PluginProblems
    {
        private readonly ConcurrentDictionary<(string PluginId, PluginPart Part), string> _problems = new ConcurrentDictionary<(string PluginId, PluginPart Part), string>();

        /// <summary>Says, as a sentence, why the part of the plugin <paramref name="pluginId"/> does not run.</summary>
        public void Report(string pluginId, PluginPart part, string problem)
        {
            _problems[(pluginId, part)] = problem;
        }

        /// <summary>Says that the part runs.</summary>
        public void Clear(string pluginId, PluginPart part)
        {
            _problems.TryRemove((pluginId, part), out _);
        }

        /// <summary>Why the part does not run; null for one that does.</summary>
        public string? Of(string pluginId, PluginPart part)
        {
            return _problems.GetValueOrDefault((pluginId, part));
        }
    }
}
