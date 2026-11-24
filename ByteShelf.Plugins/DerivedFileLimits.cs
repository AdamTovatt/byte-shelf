using ByteShelfCommon;

namespace ByteShelf.Plugins
{
    /// <summary>How large derived files may be.</summary>
    public static class DerivedFileLimits
    {
        private const long MiB = 1024 * 1024;

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
    }
}
