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

        /// <summary>The entries after <paramref name="afterSequence"/>, oldest first, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null);

        /// <summary>The newest sequence number, or 0 when the log is empty.</summary>
        Task<long> GetLatestSequenceAsync(IDbSession? session = null);
    }
}
