namespace ByteShelfCommon
{
    /// <summary>The primary a replica follows, as the replica's admins see it. Never the credential.</summary>
    public sealed class ReplicationSourceInfo
    {
        /// <summary>The primary's public URL.</summary>
        public required string PrimaryUrl { get; init; }

        /// <summary>When this server was paired with it.</summary>
        public required DateTimeOffset PairedAt { get; init; }

        /// <summary>The primary's newest change applied here.</summary>
        public required long AppliedSequence { get; init; }

        /// <summary>Whether it is taking a whole copy rather than following the log.</summary>
        public required bool Copying { get; init; }

        /// <summary>When a step last succeeded; null before the first did.</summary>
        public DateTimeOffset? LastSyncedAt { get; init; }

        /// <summary>Why the last step failed; null once one succeeds.</summary>
        public string? LastError { get; init; }

        /// <summary>When the last step failed.</summary>
        public DateTimeOffset? LastErrorAt { get; init; }
    }
}
