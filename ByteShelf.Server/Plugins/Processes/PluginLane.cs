namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Which of a plugin's processes, and turns, a run takes, and how long it may take: a plugin has one process and one
    /// turn at a time in each lane, so what goes on for hours in one never holds up the other.
    /// </summary>
    /// <remarks>
    /// Each process may take <see cref="PluginMemory.MaxBytes"/>, so a plugin with runs in both lanes may take twice that,
    /// with what its processes start beside it; a long-lane process exists only while a plugin makes a stream, and ends
    /// with its run (<see cref="KeepsProcess"/>). Kept apart rather than under one budget, which would take a cgroup the server does
    /// not have (DEPLOYMENT.md says so to operators).
    /// </remarks>
    public sealed record PluginLane
    {
        /// <summary>Runs of a minute at most: reading contents, and making what someone may be waiting for.</summary>
        public static readonly PluginLane Quick = new PluginLane("quick", TimeSpan.FromSeconds(60), lowestPriority: false, keepsProcess: true);

        /// <summary>Runs that may take a day, such as remaking a long video, at the lowest priority so the server stays responsive.</summary>
        /// <remarks>
        /// The day is the clock's, not the processor's, so a host kept busy for long may end a run that would have
        /// finished, which then fails as any run does and is not tried again until its version changes. Kept so on
        /// purpose: a run that is tried again after its limit would take another day of a busy host each pass, for
        /// contents that may never finish. A restart of the server ends a run here with nothing kept, so the next pass
        /// starts it over from the beginning.
        /// </remarks>
        public static readonly PluginLane Long = new PluginLane("long", TimeSpan.FromHours(24), lowestPriority: true, keepsProcess: false);

        public static readonly IReadOnlyList<PluginLane> All = [Quick, Long];

        private PluginLane(string name, TimeSpan runLimit, bool lowestPriority, bool keepsProcess)
        {
            Name = name;
            RunLimit = runLimit;
            LowestPriority = lowestPriority;
            KeepsProcess = keepsProcess;
        }

        /// <summary>For logs.</summary>
        public string Name { get; }

        /// <summary>How long a run may take; over it, the process is killed and the run has failed.</summary>
        public TimeSpan RunLimit { get; }

        /// <summary>Whether its process, and every process it starts, runs at the lowest CPU priority (nice 19).</summary>
        public bool LowestPriority { get; }

        /// <summary>
        /// Whether its process goes on to the next run, rather than ending with each: one that has read contents is ended
        /// when those are removed, so a process kept for runs of hours would have one over other contents start over.
        /// </summary>
        public bool KeepsProcess { get; }
    }
}
