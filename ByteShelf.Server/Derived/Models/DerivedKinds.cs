using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What the server knows of the kinds of derived files.</summary>
    public static class DerivedKinds
    {
        public static readonly IReadOnlyList<DerivedKind> All = Enum.GetValues<DerivedKind>();

        private static readonly MakingRules Thumbnails = new MakingRules(JobNames.DerivedMaking, MadeWhenAsked: true, PluginLane.Quick);
        private static readonly MakingRules WhenAsked = new MakingRules(Job: null, MadeWhenAsked: true, PluginLane.Quick);
        private static readonly MakingRules Streams = new MakingRules(JobNames.StreamMaking, MadeWhenAsked: false, PluginLane.Long);

        /// <summary>
        /// The groups made for every file that has them, in the background: of contents as they are stored, and of those
        /// stored already. Each goes at its own pace, so a long one holds up none of the others.
        /// </summary>
        public static readonly IReadOnlyList<MakingGroup> InBackground = Enum.GetValues<MakingGroup>().Where(group => RulesOf(group).Job != null).ToList();

        /// <summary>The run <paramref name="kind"/> is made in.</summary>
        public static MakingGroup GroupOf(DerivedKind kind)
        {
            return kind switch
            {
                DerivedKind.Thumbnail or DerivedKind.Preview => MakingGroup.Background,
                DerivedKind.Full => MakingGroup.Full,
                DerivedKind.Agent => MakingGroup.Agent,
                DerivedKind.Stream => MakingGroup.Stream,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no such kind of derived file."),
            };
        }

        /// <summary>How <paramref name="group"/> is made: the one place that says so.</summary>
        public static MakingRules RulesOf(MakingGroup group)
        {
            return group switch
            {
                MakingGroup.Background => Thumbnails,
                MakingGroup.Full or MakingGroup.Agent => WhenAsked,
                MakingGroup.Stream => Streams,
                _ => throw new ArgumentOutOfRangeException(nameof(group), group, "There is no such group of derived files."),
            };
        }

        /// <summary>The kinds made in one run.</summary>
        public static IReadOnlyList<DerivedKind> In(MakingGroup group)
        {
            return All.Where(kind => GroupOf(kind) == group).ToList();
        }

        /// <summary>The kind as it is written in paths and the database.</summary>
        public static string NameOf(DerivedKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        /// <summary>The kind <see cref="NameOf"/> wrote as <paramref name="name"/>.</summary>
        /// <exception cref="ArgumentException">No kind is written so.</exception>
        public static DerivedKind Parse(string name)
        {
            foreach (DerivedKind kind in All)
            {
                if (NameOf(kind) == name)
                {
                    return kind;
                }
            }

            throw new ArgumentException($"No kind of derived file is written '{name}'.", nameof(name));
        }
    }
}
