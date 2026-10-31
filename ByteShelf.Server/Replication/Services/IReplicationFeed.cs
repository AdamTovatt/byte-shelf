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

        /// <summary>A page of a whole copy: the newest state of every entity that exists, after <paramref name="afterSequence"/>.</summary>
        Task<ChangeFeedPage> GetCopyAsync(long afterSequence, int limit);

        /// <summary>Where the bytes of a file (live or in the trash) are; null once it has been purged.</summary>
        Task<string?> GetObjectPathAsync(Guid fileId);
    }
}
