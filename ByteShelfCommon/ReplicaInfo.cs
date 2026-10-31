namespace ByteShelfCommon
{
    /// <summary>A replica as its primary's admins see it: when it was last heard from and how far behind it is.</summary>
    public sealed class ReplicaInfo
    {
        /// <summary>The replica's id on the primary.</summary>
        public required Guid Id { get; init; }

        /// <summary>The name its admin gave it when pairing.</summary>
        public required string Name { get; init; }

        /// <summary>When it was paired.</summary>
        public required DateTimeOffset PairedAt { get; init; }

        /// <summary>When it last asked for changes; null before it first did.</summary>
        public DateTimeOffset? LastSeenAt { get; init; }

        /// <summary>The newest change it had applied, as of its last request.</summary>
        public required long AppliedSequence { get; init; }

        /// <summary>How many changes it has yet to apply, as of its last request.</summary>
        public required long ChangesBehind { get; init; }
    }
}
