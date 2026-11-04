namespace ByteShelf.Server.Replication.Models
{
    /// <summary>On a replica: the primary it follows, the credential it presents there, and its progress.</summary>
    public class ReplicationSource
    {
        public required string PrimaryUrl { get; init; }

        public required string PrimaryInstanceId { get; init; }

        public required string Credential { get; init; }

        public required DateTimeOffset PairedAt { get; init; }

        /// <summary>The primary's change-log sequence everything up to which is applied here.</summary>
        public required long AppliedSequence { get; init; }

        /// <summary>
        /// <see cref="ChangeLog.Models.ChangeLogEntry.GetFingerprint"/> of the entry at <see cref="AppliedSequence"/>,
        /// so the primary can tell when that number now names another entry; null after a copy until the next entry.
        /// </summary>
        public string? AppliedFingerprint { get; init; }

        /// <summary>Whether a whole copy is due before following the log: at first, and after falling too far behind.</summary>
        public required bool NeedsCopy { get; init; }

        public DateTimeOffset? LastSyncedAt { get; init; }

        public string? LastError { get; init; }

        public DateTimeOffset? LastErrorAt { get; init; }

        /// <summary>When the steps failing since the last one that worked began failing; null while the last step worked.</summary>
        public DateTimeOffset? FailingSince { get; init; }
    }
}
