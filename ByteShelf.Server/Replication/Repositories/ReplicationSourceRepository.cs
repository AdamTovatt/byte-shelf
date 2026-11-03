using System.Data.Common;
using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ReplicationSourceRepository : RepositoryBase, IReplicationSourceRepository
    {
        private const string SelectColumns = $@"
            primary_url AS {nameof(SourceEntity.PrimaryUrl)},
            primary_instance_id AS {nameof(SourceEntity.PrimaryInstanceId)},
            credential,
            paired_at AS {nameof(SourceEntity.PairedAt)},
            applied_sequence AS {nameof(SourceEntity.AppliedSequence)},
            applied_fingerprint AS {nameof(SourceEntity.AppliedFingerprint)},
            needs_copy AS {nameof(SourceEntity.NeedsCopy)},
            last_synced_at AS {nameof(SourceEntity.LastSyncedAt)},
            last_error AS {nameof(SourceEntity.LastError)},
            last_error_at AS {nameof(SourceEntity.LastErrorAt)},
            failing_since AS {nameof(SourceEntity.FailingSince)}";

        public ReplicationSourceRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<ReplicationSource?> GetAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                SourceEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<SourceEntity>(
                    $"SELECT {SelectColumns} FROM replication_source", transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> TryInsertAsync(ReplicationSource source, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = @"
                    INSERT INTO replication_source (singleton, primary_url, primary_instance_id, credential, paired_at, applied_sequence, applied_fingerprint, needs_copy)
                    VALUES (1, @PrimaryUrl, @PrimaryInstanceId, @Credential, @PairedAt, @AppliedSequence, @AppliedFingerprint, @NeedsCopy)
                    ON CONFLICT (singleton) DO NOTHING";

                return await dbSession.Connection.ExecuteAsync(
                    query,
                    new { source.PrimaryUrl, source.PrimaryInstanceId, source.Credential, source.PairedAt, source.AppliedSequence, source.AppliedFingerprint, source.NeedsCopy },
                    transaction: dbSession.Transaction) > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> SetProgressAsync(ReplicationSource source, long appliedSequence, string? appliedFingerprint, DateTimeOffset syncedAt, IDbSession session)
        {
            string credential = source.Credential;
            string query = $@"
                UPDATE replication_source
                SET applied_sequence = @{nameof(appliedSequence)}, applied_fingerprint = @{nameof(appliedFingerprint)}, needs_copy = 0,
                    last_synced_at = @{nameof(syncedAt)}, last_error = NULL, last_error_at = NULL, failing_since = NULL
                WHERE credential = @{nameof(credential)}";

            return await session.Connection.ExecuteAsync(query, new { appliedSequence, appliedFingerprint, syncedAt, credential }, transaction: session.Transaction) > 0;
        }

        /// <inheritdoc />
        public async Task RecordSyncErrorAsync(string message, DateTimeOffset at, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"UPDATE replication_source SET last_error = @{nameof(message)}, last_error_at = @{nameof(at)}, failing_since = COALESCE(failing_since, @{nameof(at)})",
                    new { message, at },
                    transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task RequireCopyAsync(IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync("UPDATE replication_source SET needs_copy = 1", transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync("DELETE FROM replication_source", transaction: dbSession.Transaction) > 0;
            }, session);
        }

        internal class SourceEntity
        {
            public string PrimaryUrl { get; set; } = string.Empty;
            public string PrimaryInstanceId { get; set; } = string.Empty;
            public string Credential { get; set; } = string.Empty;
            public DateTimeOffset PairedAt { get; set; }
            public long AppliedSequence { get; set; }
            public string? AppliedFingerprint { get; set; }
            public bool NeedsCopy { get; set; }
            public DateTimeOffset? LastSyncedAt { get; set; }
            public string? LastError { get; set; }
            public DateTimeOffset? LastErrorAt { get; set; }
            public DateTimeOffset? FailingSince { get; set; }

            public ReplicationSource ToDomain()
            {
                return new ReplicationSource
                {
                    PrimaryUrl = PrimaryUrl,
                    PrimaryInstanceId = PrimaryInstanceId,
                    Credential = Credential,
                    PairedAt = PairedAt,
                    AppliedSequence = AppliedSequence,
                    AppliedFingerprint = AppliedFingerprint,
                    NeedsCopy = NeedsCopy,
                    LastSyncedAt = LastSyncedAt,
                    LastError = LastError,
                    LastErrorAt = LastErrorAt,
                    FailingSince = FailingSince,
                };
            }
        }
    }
}
