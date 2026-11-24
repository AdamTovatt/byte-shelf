using System.Collections.Concurrent;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Which readers of files' contents are not run until the server restarts, and why: whatever runs one
    /// reports it here, and admins see it with the plugin. A reason holds nothing of what was read.
    /// </summary>
    public sealed class ReaderProblems
    {
        private readonly ConcurrentDictionary<string, string> _problems = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Says, as a sentence, why the reader <paramref name="readerId"/> (<see cref="ContentReader.Id"/>, a plugin's id) is not run any more.</summary>
        public void Report(string readerId, string problem)
        {
            _problems[readerId] = problem;
        }

        /// <summary>Why the reader is not run any more; null for one that is.</summary>
        public string? Of(string readerId)
        {
            return _problems.GetValueOrDefault(readerId);
        }
    }
}
