namespace ByteShelfCommon
{
    /// <summary>
    /// A link anyone holding it can store one file through, at the path it was made for, without
    /// signing in, until it runs out: <c>PUT</c> the contents to <see cref="Url"/> with a
    /// <c>Content-Length</c>. It acts with the access of whoever made it, checked again when it is used,
    /// so it stops working if that access is taken away. A link that may not overwrite is refused when
    /// the path is taken, so it stores at most one file.
    /// </summary>
    public sealed record UploadLink
    {
        /// <summary>How long an upload link may work: 15 minutes by default, long enough to hand on and use, and at most 24 hours.</summary>
        public static readonly LinkLifetime Lifetime = new LinkLifetime(TimeSpan.FromMinutes(15), TimeSpan.FromHours(24));

        /// <summary>
        /// The link. The server answers it on its public URL when it has one configured, and otherwise
        /// relative to its own root; <c>ByteShelfHttpClient</c> makes a relative one absolute on the
        /// address it reached the server at.
        /// </summary>
        public required string Url { get; init; }

        /// <summary>When the link stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>Where the file sent to it is stored.</summary>
        public required string Path { get; init; }

        /// <summary>Whether a file already at <see cref="Path"/> is replaced; refused otherwise.</summary>
        public required bool Overwrite { get; init; }
    }
}
