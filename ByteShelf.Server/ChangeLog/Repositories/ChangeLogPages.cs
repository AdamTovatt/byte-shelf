using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.ChangeLog.Repositories
{
    /// <summary>Reads a page of entries from any table in the change log's shape, loading no entry beyond what the page holds.</summary>
    internal static class ChangeLogPages
    {
        /// <param name="table">The table, which the query names <c>entry</c>.</param>
        /// <param name="condition">A condition on <c>entry</c> that every entry of the page meets.</param>
        /// <param name="after">The sequence the page starts after.</param>
        /// <param name="limit">What the page holds.</param>
        /// <param name="session">A transaction, so the page is one snapshot.</param>
        public static async Task<ChangeLogPage> ReadAsync(string table, string condition, long after, ChangeLogPageLimit limit, IDbSession session)
        {
            int candidateLimit = limit.Entries + 1;
            string sizesQuery = $@"
                SELECT sequence, coalesce(length(CAST(payload AS BLOB)), 0) AS {nameof(SizeEntity.Bytes)}
                FROM {table} AS entry
                WHERE entry.sequence > @{nameof(after)} AND {condition}
                ORDER BY entry.sequence
                LIMIT @{nameof(candidateLimit)}";

            List<SizeEntity> candidates = (await session.Connection.QueryAsync<SizeEntity>(
                sizesQuery, new { after, candidateLimit }, transaction: session.Transaction)).ToList();

            if (candidates.Count == 0)
            {
                return new ChangeLogPage([], HasMore: false);
            }

            int count = 1;
            long bytes = candidates[0].Bytes;
            while (count < Math.Min(candidates.Count, limit.Entries) && bytes + candidates[count].Bytes <= limit.PayloadBytes)
            {
                bytes += candidates[count].Bytes;
                count++;
            }

            long last = candidates[count - 1].Sequence;
            string query = $@"
                SELECT {ChangeLogRepository.SelectColumns}
                FROM {table} AS entry
                WHERE entry.sequence > @{nameof(after)} AND entry.sequence <= @{nameof(last)} AND {condition}
                ORDER BY entry.sequence";

            IEnumerable<ChangeLogRepository.ChangeLogEntity> entities = await session.Connection.QueryAsync<ChangeLogRepository.ChangeLogEntity>(
                query, new { after, last }, transaction: session.Transaction);

            return new ChangeLogPage(entities.Select(entity => entity.ToDomain()).ToList(), HasMore: count < candidates.Count);
        }

        internal sealed class SizeEntity
        {
            public long Sequence { get; set; }
            public long Bytes { get; set; }
        }
    }
}
