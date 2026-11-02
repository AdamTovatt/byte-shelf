namespace ByteShelfCommon
{
    /// <summary>
    /// A link to one file's contents that works without signing in until it runs out: for a browser to
    /// open or save, or to share with someone who has no access to the shelf. It names that file, so a
    /// file stored over it later is a new file it does not reach; while the file is in the trash the
    /// link answers not found, and once the file is restored it works again until it runs out.
    /// </summary>
    public sealed record DownloadLink
    {
        /// <summary>How long a download link may work: a day by default, long enough to pass on to someone, and at most 7 days.</summary>
        public static readonly LinkLifetime Lifetime = new LinkLifetime(TimeSpan.FromHours(24), TimeSpan.FromDays(7));

        /// <summary>
        /// The link. The server answers it on its public URL when it has one configured, and otherwise
        /// relative to its own root; <c>ByteShelfHttpClient</c> makes a relative one absolute on the
        /// address it reached the server at. Add <c>?download=true</c> to have a browser save the file
        /// rather than show it.
        /// </summary>
        public required string Url { get; init; }

        /// <summary>When the link stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
