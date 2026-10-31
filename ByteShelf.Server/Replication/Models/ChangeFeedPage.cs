using ByteShelf.Server.ChangeLog.Models;

namespace ByteShelf.Server.Replication.Models
{
    /// <summary>
    /// A page of the primary's change log, or of a whole copy, for a replica: entries oldest first,
    /// whether more follow, the newest sequence there is, and whether the replica has to take a whole
    /// copy instead (it is behind something compaction dropped, or the log no longer holds the entry it
    /// applied last, as after the primary was restored from a backup).
    /// </summary>
    public class ChangeFeedPage
    {
        public required string InstanceId { get; init; }

        public required IReadOnlyList<ChangeLogEntry> Entries { get; init; }

        /// <summary>Whether entries follow this page's last one. A short page does not say so: a primary may give smaller pages than asked for.</summary>
        public required bool HasMore { get; init; }

        public required long LatestSequence { get; init; }

        public required bool CopyNeeded { get; init; }
    }
}
