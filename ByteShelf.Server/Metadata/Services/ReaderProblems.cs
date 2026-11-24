using System.Collections.Concurrent;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Which plugins' readers of files' contents could not run when they last had something to read, and why:
    /// admins see it with the plugin. A reason holds nothing of what was read.
    /// </summary>
    public sealed class ReaderProblems
    {
        private readonly ConcurrentDictionary<string, string> _problems = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Says, as a sentence, why the reader of the plugin <paramref name="pluginId"/> does not run.</summary>
        public void Report(string pluginId, string problem)
        {
            _problems[pluginId] = problem;
        }

        /// <summary>Says that the reader runs.</summary>
        public void Clear(string pluginId)
        {
            _problems.TryRemove(pluginId, out _);
        }

        /// <summary>Why the reader does not run; null for one that does.</summary>
        public string? Of(string pluginId)
        {
            return _problems.GetValueOrDefault(pluginId);
        }
    }
}
