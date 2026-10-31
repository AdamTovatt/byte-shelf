namespace ByteShelfCommon
{
    /// <summary>
    /// A server's part in replication: the replicas paired with it, or, on a replica, the primary it
    /// follows (a replica has no replicas of its own).
    /// </summary>
    public sealed class ReplicationStatus
    {
        /// <summary>The newest change this server has logged.</summary>
        public required long LatestSequence { get; init; }

        /// <summary>The replicas paired with this server, oldest first.</summary>
        public required IReadOnlyList<ReplicaInfo> Replicas { get; init; }

        /// <summary>The primary this server follows; null unless it is a replica.</summary>
        public ReplicationSourceInfo? Source { get; init; }
    }
}
