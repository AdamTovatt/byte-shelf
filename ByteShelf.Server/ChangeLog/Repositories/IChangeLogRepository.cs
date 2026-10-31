using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.ChangeLog.Repositories
{
    public interface IChangeLogRepository
    {
        /// <summary>
        /// Records a change with the entity's whole state afterwards, or null once it is gone. Called by
        /// the repositories' own write methods with the session of the transaction that made the change,
        /// so a write and its entry commit or roll back together and no write path can leave one out.
        /// </summary>
        Task AppendAsync<TState>(string entityType, Guid entityId, string action, TState? state, DateTimeOffset at, IDbSession session)
            where TState : class;

        /// <summary>
        /// Runs <paramref name="deleteReturningIds"/>, a <c>DELETE</c> ending in <c>RETURNING id</c>, and
        /// logs every row it removed as purged, in the same transaction. Answers how many it removed.
        /// </summary>
        Task<int> DeleteAndLogAsync(string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session);

        /// <summary>The entries after <paramref name="afterSequence"/>, oldest first, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null);

        /// <summary>The newest sequence number ever given, or 0 before the first entry; compaction never lowers it.</summary>
        Task<long> GetLatestSequenceAsync(IDbSession? session = null);

        /// <summary>
        /// The newest entry of each entity that still exists, oldest first, after <paramref name="afterSequence"/>
        /// and at most <paramref name="limit"/>: a whole copy of the state, read page by page.
        /// </summary>
        Task<IReadOnlyList<ChangeLogEntry>> GetLatestStatesAfterAsync(long afterSequence, int limit, IDbSession? session = null);

        /// <summary>
        /// Compacts the log: drops entries made before <paramref name="before"/> that a newer entry for the
        /// same entity supersedes, and removals made before it, in <paramref name="session"/>'s
        /// transaction, and records the highest sequence dropped. Answers how many were dropped.
        /// </summary>
        Task<int> CompactAsync(DateTimeOffset before, IDbSession session);

        /// <summary>
        /// The highest sequence compaction dropped. A follower behind it cannot replay the log: it may miss
        /// a removal, or meet a row before the only entry left for the parent it names.
        /// </summary>
        Task<long> GetPrunedThroughAsync(IDbSession? session = null);

        /// <summary>The entry numbered <paramref name="sequence"/>, or null when there is none (never made, or dropped).</summary>
        Task<ChangeLogEntry?> GetBySequenceAsync(long sequence, IDbSession? session = null);
    }
}
