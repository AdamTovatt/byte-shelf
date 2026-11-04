using System.Data.Common;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class TrashRepository : RepositoryBase, ITrashRepository
    {
        // A deletion's top entry is the one row of it whose container is not part of the same deletion:
        // the deleted file, or the directory a subtree was deleted from. Its totals count every file the
        // deletion took. The kind column holds the TrashEntryKind value.
        private static readonly string EntriesQuery = $@"
            WITH top_entry AS (
                SELECT {(int)TrashEntryKind.Directory} AS kind, d.id AS entry_id, d.deletion_id, d.deleted_at, d.parent_id AS container_id, d.name
                FROM directory d
                WHERE d.shelf_id = @shelfId AND d.deletion_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM directory p WHERE p.id = d.parent_id AND p.deletion_id = d.deletion_id)
                UNION ALL
                SELECT {(int)TrashEntryKind.File}, f.id, f.deletion_id, f.deleted_at, f.directory_id, f.name
                FROM file f
                WHERE f.shelf_id = @shelfId AND f.deletion_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM directory p WHERE p.id = f.directory_id AND p.deletion_id = f.deletion_id)
            ),
            total AS (
                SELECT deletion_id, COUNT(*) AS file_count, SUM(size_bytes) AS size_bytes
                FROM file
                WHERE shelf_id = @shelfId AND deletion_id IS NOT NULL
                GROUP BY deletion_id
            )
            SELECT
                t.kind AS {nameof(TrashedEntryEntity.Kind)},
                t.entry_id AS {nameof(TrashedEntryEntity.EntryId)},
                t.deletion_id AS {nameof(TrashedEntryEntity.DeletionId)},
                t.deleted_at AS {nameof(TrashedEntryEntity.DeletedAt)},
                t.container_id AS {nameof(TrashedEntryEntity.ContainerId)},
                t.name AS {nameof(TrashedEntryEntity.Name)},
                COALESCE(total.file_count, 0) AS {nameof(TrashedEntryEntity.FileCount)},
                COALESCE(total.size_bytes, 0) AS {nameof(TrashedEntryEntity.SizeBytes)}
            FROM top_entry t
            LEFT JOIN total ON total.deletion_id = t.deletion_id";

        public TrashRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TrashedEntry>> GetEntriesAsync(Guid shelfId, DateTimeOffset deletedSince, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"{EntriesQuery} WHERE t.deleted_at >= @{nameof(deletedSince)} ORDER BY t.deleted_at DESC, t.name";

                IEnumerable<TrashedEntryEntity> entities = await dbSession.Connection.QueryAsync<TrashedEntryEntity>(
                    query, new { shelfId, deletedSince }, transaction: dbSession.Transaction);

                return (IReadOnlyList<TrashedEntry>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<TrashedEntry?> GetEntryAsync(Guid shelfId, Guid deletionId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"{EntriesQuery} WHERE t.deletion_id = @{nameof(deletionId)}";

                TrashedEntryEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<TrashedEntryEntity>(
                    query, new { shelfId, deletionId }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        internal class TrashedEntryEntity
        {
            public TrashEntryKind Kind { get; set; }
            public Guid EntryId { get; set; }
            public Guid DeletionId { get; set; }
            public DateTimeOffset DeletedAt { get; set; }
            public Guid ContainerId { get; set; }
            public string Name { get; set; } = string.Empty;
            public long FileCount { get; set; }
            public long SizeBytes { get; set; }

            public TrashedEntry ToDomain()
            {
                return new TrashedEntry
                {
                    Kind = Kind,
                    EntryId = EntryId,
                    DeletionId = DeletionId,
                    DeletedAt = DeletedAt,
                    ContainerId = ContainerId,
                    Name = Name,
                    FileCount = (int)FileCount,
                    SizeBytes = SizeBytes,
                };
            }
        }
    }
}
