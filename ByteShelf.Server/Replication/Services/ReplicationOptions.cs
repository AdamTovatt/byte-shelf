namespace ByteShelf.Server.Replication.Services
{
    /// <summary>Where replicas reach this server, and the limits replication works within.</summary>
    public sealed class ReplicationOptions
    {
        /// <summary>How long a pairing code can be used.</summary>
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

        /// <summary>This server's public URL (<c>APP_BASE_URL</c>), which a pairing code made here names.</summary>
        public required string PublicUrl { get; init; }

        /// <summary>
        /// How long a call to the primary may go without anything arriving, before its answer starts or
        /// between parts of it, before it is given up: a connection that died without closing never ends.
        /// </summary>
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How many entries a replica asks its primary for, and applies, at a time; the primary may give
        /// fewer (at most <see cref="IReplicationFeed.MaxPageSize"/>).
        /// </summary>
        public int PageSize { get; init; } = 500;
    }
}
