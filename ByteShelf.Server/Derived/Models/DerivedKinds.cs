using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What the server knows of the kinds of derived files.</summary>
    public static class DerivedKinds
    {
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
