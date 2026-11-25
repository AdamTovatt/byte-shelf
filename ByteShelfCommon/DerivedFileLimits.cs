namespace ByteShelfCommon
{
    /// <summary>How large derived files may be.</summary>
    public static class DerivedFileLimits
    {
        private const long MiB = 1024 * 1024;

        /// <summary>
        /// The most bytes a derived file of <paramref name="kind"/> may take. A shelf's quota counts what is kept of each
        /// file's contents, so this also bounds how far past its own bytes one stored file may bring its shelf's usage.
        /// </summary>
        /// <remarks>
        /// An <see cref="DerivedKind.Agent"/> file is what an agent is shown inline, which an agent's host may refuse past
        /// this: so the server's own MCP tools show an image inline only within it, too.
        /// </remarks>
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
