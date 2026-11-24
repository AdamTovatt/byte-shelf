namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>How long a reader of contents may take; written out rather than taken from the code, so a change of one is noticed.</summary>
    internal static class TestReadLimits
    {
        public static readonly TimeSpan Prepare = TimeSpan.FromSeconds(60);

        public static readonly TimeSpan Read = TimeSpan.FromSeconds(60);

        public static readonly TimeSpan Stop = TimeSpan.FromSeconds(5);

        /// <summary>How long a test waits, by the real clock, for what must happen.</summary>
        public static readonly TimeSpan Real = TimeSpan.FromSeconds(30);
    }
}
