using ByteShelf.Server.Replication.Models;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>What a primary serves its replicas: its change log, a whole copy read from it, and files' bytes.</summary>
    public interface IReplicationFeed
    {
        /// <summary>The most entries one page holds.</summary>
        public const int MaxPageSize = 1000;

        /// <summary>
        /// The log after <paramref name="afterSequence"/>, which is also how far <paramref name="replica"/>
        /// has applied it, so this records that. A replica behind something compaction dropped, ahead of the
        /// log, or whose last entry (<paramref name="afterFingerprint"/>) the log no longer holds under that
        /// number, is told to take a copy instead.
        /// </summary>
        Task<ChangeFeedPage> GetChangesAsync(Replica replica, long afterSequence, string? afterFingerprint, int limit);

        /// <summary>
        /// A page of a whole copy: the newest entry of every entity after <paramref name="afterSequence"/>,
        /// its state, or its removal once it no longer exists. Records that <paramref name="replica"/> is
        /// copying, so it is not counted behind meanwhile.
        /// </summary>
        Task<ChangeFeedPage> GetCopyAsync(Replica replica, long afterSequence, int limit);

        /// <summary>
        /// Where the bytes of a file (live or in the trash) are, while they are still
        /// <paramref name="sha256"/>; null once it has been purged or its contents swapped for others.
        /// Records that <paramref name="replica"/> was seen, as a copy may fetch files for long between pages.
        /// </summary>
        Task<string?> GetObjectPathAsync(Replica replica, Guid fileId, string sha256);
    }
}
