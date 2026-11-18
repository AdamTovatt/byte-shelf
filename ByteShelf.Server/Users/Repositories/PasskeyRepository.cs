using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Users.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <inheritdoc cref="IPasskeyRepository" />
    public class PasskeyRepository : RepositoryBase, IPasskeyRepository
    {
        private const string SelectColumns = $@"
            id,
            user_id AS {nameof(PasskeyEntity.UserId)},
            credential_id AS {nameof(PasskeyEntity.CredentialId)},
            public_key AS {nameof(PasskeyEntity.PublicKey)},
            transports AS {nameof(PasskeyEntity.TransportsJson)},
            name,
            created_at AS {nameof(PasskeyEntity.CreatedAt)},
            sign_count AS {nameof(PasskeyEntity.SignCount)},
            last_used_at AS {nameof(PasskeyEntity.LastUsedAt)}";

        /// <summary>The logged columns only, so a replicated write keeps this server's sign count and last use.</summary>
        private const string InsertSql = @"
            INSERT INTO passkey (id, user_id, credential_id, public_key, transports, name, created_at)
            VALUES (@Id, @UserId, @CredentialId, @PublicKey, @Transports, @Name, @CreatedAt)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        private readonly IChangeLogRepository _changeLog;

        public PasskeyRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<StoredPasskey?> GetByCredentialIdAsync(string credentialId)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM passkey WHERE credential_id = @{nameof(credentialId)}";

                PasskeyEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<PasskeyEntity>(
                    query, new { credentialId }, transaction: dbSession.Transaction);

                return entity?.ToStored();
            });
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<StoredPasskey>> GetForUserAsync(Guid userId)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM passkey WHERE user_id = @{nameof(userId)} ORDER BY created_at, id";

                IEnumerable<PasskeyEntity> entities = await dbSession.Connection.QueryAsync<PasskeyEntity>(
                    query, new { userId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<StoredPasskey>)entities.Select(entity => entity.ToStored()).ToList();
            });
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(Guid userId, Guid passkeyId)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT EXISTS (SELECT 1 FROM passkey WHERE id = @{nameof(passkeyId)} AND user_id = @{nameof(userId)})";

                return await dbSession.Connection.ExecuteScalarAsync<bool>(
                    query, new { userId, passkeyId }, transaction: dbSession.Transaction);
            });
        }

        /// <inheritdoc />
        public async Task InsertAsync(Passkey passkey, IDbSession session)
        {
            await session.Connection.ExecuteAsync(InsertSql, Parameters(passkey), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Passkey, passkey.Id, ChangeAction.Added, passkey, passkey.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> RenameAsync(Guid userId, Guid passkeyId, string name, DateTimeOffset at, IDbSession session)
        {
            string query = $@"
                UPDATE passkey
                SET name = @{nameof(name)}
                WHERE id = @{nameof(passkeyId)} AND user_id = @{nameof(userId)}
                RETURNING {SelectColumns}";

            PasskeyEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<PasskeyEntity>(
                query, new { userId, passkeyId, name }, transaction: session.Transaction);

            if (updated == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.Passkey, updated.Id, ChangeAction.Updated, updated.ToStored().Passkey, at, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid userId, Guid passkeyId, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM passkey WHERE id = @{nameof(passkeyId)} AND user_id = @{nameof(userId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.Passkey, query, new { userId, passkeyId }, at, session) > 0;
        }

        /// <inheritdoc />
        public async Task DeleteForUserAsync(Guid userId, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM passkey WHERE user_id = @{nameof(userId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.Passkey, query, new { userId }, at, session);
        }

        /// <inheritdoc />
        public async Task RecordSignInAsync(Guid passkeyId, uint signCount, DateTimeOffset at)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                long count = signCount;
                string query = $@"
                    UPDATE passkey
                    SET sign_count = MAX(sign_count, @{nameof(count)}), last_used_at = @{nameof(at)}
                    WHERE id = @{nameof(passkeyId)}";

                await dbSession.Connection.ExecuteAsync(query, new { passkeyId, count, at }, transaction: dbSession.Transaction);
            });
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(Passkey passkey, string action, DateTimeOffset at, IDbSession session)
        {
            string asideQuery = "DELETE FROM passkey WHERE credential_id = @CredentialId AND id <> @Id RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.Passkey, asideQuery, new { passkey.CredentialId, passkey.Id }, at, session);

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(passkey), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Passkey, passkey.Id, action, passkey, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM passkey WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.Passkey, query, new { id }, at, session) > 0;
        }

        private static object Parameters(Passkey passkey)
        {
            return new
            {
                passkey.Id,
                passkey.UserId,
                passkey.CredentialId,
                passkey.PublicKey,
                Transports = passkey.Transports == null ? null : JsonSerializer.Serialize(passkey.Transports),
                passkey.Name,
                passkey.CreatedAt,
            };
        }

        internal class PasskeyEntity
        {
            public Guid Id { get; set; }
            public Guid UserId { get; set; }
            public string CredentialId { get; set; } = string.Empty;
            public string PublicKey { get; set; } = string.Empty;
            public string? TransportsJson { get; set; }
            public string Name { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public long SignCount { get; set; }
            public DateTimeOffset? LastUsedAt { get; set; }

            public StoredPasskey ToStored()
            {
                return new StoredPasskey
                {
                    Passkey = new Passkey
                    {
                        Id = Id,
                        UserId = UserId,
                        CredentialId = CredentialId,
                        PublicKey = PublicKey,
                        Transports = TransportsJson == null ? null : JsonSerializer.Deserialize<List<string>>(TransportsJson),
                        Name = Name,
                        CreatedAt = CreatedAt,
                    },
                    SignCount = checked((uint)SignCount),
                    LastUsedAt = LastUsedAt,
                };
            }
        }
    }
}
