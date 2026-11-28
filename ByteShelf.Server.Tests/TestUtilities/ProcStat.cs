using System.Globalization;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>What Linux says of a process or thread in <c>/proc/&lt;id&gt;/stat</c>, the fields a test reads by name.</summary>
    internal sealed class ProcStat
    {
        // Fields of proc(5) counted from the state, the third, as the name before it may hold spaces.
        private const int StateField = 0;
        private const int ParentField = 1;
        private const int NiceField = 16;

        private readonly string[] _fields;

        private ProcStat(string[] fields)
        {
            _fields = fields;
        }

        /// <summary>One letter: <c>R</c> running, <c>S</c> sleeping, <c>Z</c> ended and not reaped, and so on.</summary>
        public string State => _fields[StateField];

        public int Parent => int.Parse(_fields[ParentField], CultureInfo.InvariantCulture);

        /// <summary>Its nice value: 19 is the lowest priority.</summary>
        public int Nice => int.Parse(_fields[NiceField], CultureInfo.InvariantCulture);

        /// <summary>Of the process <paramref name="processId"/>.</summary>
        /// <exception cref="IOException">There is no such process, or it ended just now.</exception>
        public static ProcStat Of(int processId)
        {
            return In($"/proc/{processId}");
        }

        /// <summary>Of each thread of the process <paramref name="processId"/>.</summary>
        public static IReadOnlyList<ProcStat> OfThreads(int processId)
        {
            return Directory.EnumerateDirectories($"/proc/{processId}/task").Select(In).ToList();
        }

        private static ProcStat In(string folder)
        {
            // "pid (name) state parent group ...", where the name may hold anything.
            string stat = File.ReadAllText(Path.Combine(folder, "stat"));
            return new ProcStat(stat[(stat.LastIndexOf(')') + 2)..].Split(' '));
        }
    }
}
