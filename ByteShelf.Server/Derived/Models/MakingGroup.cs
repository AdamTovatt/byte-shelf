using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>
    /// The kinds of derived files made in one run, from one read of the contents, and how they are made: the one place
    /// that says so. Every kind is in one group.
    /// </summary>
    public sealed class MakingGroup
    {
        /// <summary>Thumbnails and previews, made for every file that has them, and as they are asked for.</summary>
        public static readonly MakingGroup Background = new MakingGroup(
            "background", [DerivedKind.Thumbnail, DerivedKind.Preview], MakingStart.WhenAskedAndInBackground(JobNames.DerivedMaking), PluginLane.Quick);

        /// <summary><see cref="DerivedKind.Full"/>, made when asked for, and trimmed together once one is kept (<see cref="Services.DerivedFiles.TrimFullAsync"/>).</summary>
        public static readonly MakingGroup Full = new MakingGroup("full", [DerivedKind.Full], MakingStart.WhenAsked, PluginLane.Quick);

        /// <summary><see cref="DerivedKind.Agent"/>, made when asked for.</summary>
        public static readonly MakingGroup Agent = new MakingGroup("agent", [DerivedKind.Agent], MakingStart.WhenAsked, PluginLane.Quick);

        /// <summary>
        /// <see cref="DerivedKind.Stream"/>, made for every file that has one in the background alone, in the long lane: runs
        /// that may take hours, which a restart of the server starts over.
        /// </summary>
        public static readonly MakingGroup Stream = new MakingGroup(
            "stream", [DerivedKind.Stream], MakingStart.InBackground(JobNames.StreamMaking), PluginLane.Long);

        public static readonly IReadOnlyList<MakingGroup> All = [Background, Full, Agent, Stream];

        /// <summary>
        /// The groups made for every file that has them, in the background: of contents as they are stored, and of those
        /// stored already. Each goes at its own pace, so a long one holds up none of the others.
        /// </summary>
        public static readonly IReadOnlyList<MakingGroup> InBackground = [.. All.Where(group => group.Start.Job != null)];

        private MakingGroup(string name, IReadOnlyList<DerivedKind> kinds, MakingStart start, PluginLane lane)
        {
            Name = name;
            Kinds = kinds;
            Start = start;
            Lane = lane;
        }

        /// <summary>For logs.</summary>
        public string Name { get; }

        /// <summary>The kinds made in one of its runs.</summary>
        public IReadOnlyList<DerivedKind> Kinds { get; }

        /// <summary>What has it made.</summary>
        public MakingStart Start { get; }

        /// <summary>The lane its runs take (<see cref="PluginLane"/>), which says how long one may.</summary>
        public PluginLane Lane { get; }

        /// <summary>The group <paramref name="kind"/> is made in.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No group has it.</exception>
        public static MakingGroup Of(DerivedKind kind)
        {
            return All.SingleOrDefault(group => group.Kinds.Contains(kind))
                ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "No group makes such a kind of derived file.");
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
