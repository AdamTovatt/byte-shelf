using System.Collections.Concurrent;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// Which parts of plugins could not run when they last had something to do, and why, in each lane their runs take
    /// (<see cref="PluginLane"/>), so one lane's runs going well never hides the other's failing: admins see it with the
    /// plugin. A reason holds nothing of the contents it was given.
    /// </summary>
    public sealed class PluginProblems
    {
        private readonly ConcurrentDictionary<(string PluginId, PluginPart Part, PluginLane Lane), string> _problems =
            new ConcurrentDictionary<(string PluginId, PluginPart Part, PluginLane Lane), string>();

        /// <summary>Says, as a sentence, why the part of the plugin <paramref name="pluginId"/> does not run in <paramref name="lane"/>.</summary>
        public void Report(string pluginId, PluginPart part, PluginLane lane, string problem)
        {
            _problems[(pluginId, part, lane)] = problem;
        }

        /// <summary>Says that the part runs in <paramref name="lane"/>.</summary>
        public void Clear(string pluginId, PluginPart part, PluginLane lane)
        {
            _problems.TryRemove((pluginId, part, lane), out _);
        }

        /// <summary>Why the part does not run, in any lane, a sentence for each; null for one that runs in every lane.</summary>
        public string? Of(string pluginId, PluginPart part)
        {
            List<string> problems = PluginLane.All.Select(lane => Of(pluginId, part, lane)).OfType<string>().ToList();
            return problems.Count == 0 ? null : string.Join(" ", problems);
        }

        /// <summary>Why the part does not run in <paramref name="lane"/>; null when it does.</summary>
        public string? Of(string pluginId, PluginPart part, PluginLane lane)
        {
            return _problems.GetValueOrDefault((pluginId, part, lane));
        }
    }
}
