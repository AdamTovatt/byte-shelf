using System.Data.Common;
using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ReplicaRepository : RepositoryBase, IReplicaRepository
    {
        private const string SelectColumns = $@"
            id,
            name,
            paired_at AS {nameof(ReplicaEntity.PairedAt)},
            last_seen_at AS {nameof(ReplicaEntity.LastSeenAt)},
            applied_sequence AS {nameof(ReplicaEntity.AppliedSequence)},
            copying";

        public ReplicaRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task InsertInviteAsync(Guid id, string secretSha256, DateTimeOffset expiresAt, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    INSERT INTO replica_invite (id, secret_sha256, expires_at)
                    VALUES (@{nameof(id)}, @{nameof(secretSha256)}, @{nameof(expiresAt)})";

                return await dbSession.Connection.ExecuteAsync(query, new { id, secretSha256, expiresAt }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> TryConsumeInviteAsync(string secretSha256, DateTimeOffset now, IDbSession session)
        {
            await session.Connection.ExecuteAsync(
                $"DELETE FROM replica_invite WHERE expires_at <= @{nameof(now)}", new { now }, transaction: session.Transaction);

            int consumed = await session.Connection.ExecuteAsync(
                $"DELETE FROM replica_invite WHERE secret_sha256 = @{nameof(secretSha256)}", new { secretSha256 }, transaction: session.Transaction);

            return consumed > 0;
        }

        /// <inheritdoc />
        public async Task InsertReplicaAsync(Replica replica, string credentialSha256, IDbSession session)
        {
            string query = $@"
                INSERT INTO replica (id, name, credential_sha256, paired_at, applied_sequence)
                VALUES (@Id, @Name, @{nameof(credentialSha256)}, @PairedAt, @AppliedSequence)";

            await session.Connection.ExecuteAsync(
                query,
                new { replica.Id, replica.Name, credentialSha256, replica.PairedAt, replica.AppliedSequence },
                transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<Replica?> GetByCredentialAsync(string credentialSha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM replica WHERE credential_sha256 = @{nameof(credentialSha256)}";

                ReplicaEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ReplicaEntity>(
                    query, new { credentialSha256 }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Replica>> GetAllAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                IEnumerable<ReplicaEntity> entities = await dbSession.Connection.QueryAsync<ReplicaEntity>(
                    $"SELECT {SelectColumns} FROM replica ORDER BY paired_at, id", transaction: dbSession.Transaction);

                return (IReadOnlyList<Replica>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task RecordFollowingAsync(Guid id, long appliedSequence, DateTimeOffset at, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE replica
                    SET last_seen_at = @{nameof(at)}, applied_sequence = @{nameof(appliedSequence)}, copying = 0
                    WHERE id = @{nameof(id)}";

                return await dbSession.Connection.ExecuteAsync(query, new { id, appliedSequence, at }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task RecordCopyingAsync(Guid id, DateTimeOffset at, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"UPDATE replica SET last_seen_at = @{nameof(at)}, copying = 1 WHERE id = @{nameof(id)}",
                    new { id, at },
                    transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task RecordSeenAsync(Guid id, DateTimeOffset at, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"UPDATE replica SET last_seen_at = @{nameof(at)} WHERE id = @{nameof(id)}",
                    new { id, at },
                    transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"DELETE FROM replica WHERE id = @{nameof(id)}", new { id }, transaction: dbSession.Transaction) > 0;
            }, session);
        }

        internal class ReplicaEntity
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public DateTimeOffset PairedAt { get; set; }
            public DateTimeOffset? LastSeenAt { get; set; }
            public long AppliedSequence { get; set; }
            public bool Copying { get; set; }

            public Replica ToDomain()
            {
                return new Replica
                {
                    Id = Id,
                    Name = Name,
                    PairedAt = PairedAt,
                    LastSeenAt = LastSeenAt,
                    AppliedSequence = AppliedSequence,
                    Copying = Copying,
                };
            }
        }
    }
}
