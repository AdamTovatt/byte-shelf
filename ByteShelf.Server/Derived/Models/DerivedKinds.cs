using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What the server knows of the kinds of derived files.</summary>
    public static class DerivedKinds
    {
        private const long MiB = 1024 * 1024;

        public static readonly IReadOnlyList<DerivedKind> All = Enum.GetValues<DerivedKind>();

        /// <summary>The run <paramref name="kind"/> is made in.</summary>
        public static MakingGroup GroupOf(DerivedKind kind)
        {
            return kind switch
            {
                DerivedKind.Thumbnail or DerivedKind.Preview => MakingGroup.Background,
                DerivedKind.Full => MakingGroup.Full,
                DerivedKind.Agent => MakingGroup.Agent,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no such kind of derived file."),
            };
        }

        /// <summary>The kinds made in one run.</summary>
        public static IReadOnlyList<DerivedKind> In(MakingGroup group)
        {
            return All.Where(kind => GroupOf(kind) == group).ToList();
        }

        /// <summary>
        /// The most bytes a derived file of <paramref name="kind"/> may take. They are not counted in a shelf's quota,
        /// so this is what bounds the disk a stored file may take through them.
        /// </summary>
        public static long MaxBytesOf(DerivedKind kind)
        {
            return kind switch
            {
                DerivedKind.Thumbnail => MiB,
                DerivedKind.Preview => 16 * MiB,
                DerivedKind.Full => 512 * MiB,
                DerivedKind.Agent => MiB,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no such kind of derived file."),
            };
        }

        /// <summary>The kind as it is written in paths.</summary>
        public static string NameOf(DerivedKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }
    }
}
