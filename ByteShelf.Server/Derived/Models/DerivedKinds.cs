using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What the server knows of the kinds of derived files; how each is made is its group's (<see cref="MakingGroup"/>).</summary>
    public static class DerivedKinds
    {
        public static readonly IReadOnlyList<DerivedKind> All = Enum.GetValues<DerivedKind>();

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
