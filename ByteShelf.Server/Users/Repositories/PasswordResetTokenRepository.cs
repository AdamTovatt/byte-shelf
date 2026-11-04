using System.Data.Common;
using ByteShelf.Server.Users.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// SQLite implementation of <see cref="IPasswordResetTokenRepository"/> for the
    /// <c>password_reset_token</c> table.
    /// </summary>
    public class PasswordResetTokenRepository : RepositoryBase, IPasswordResetTokenRepository
    {
        public PasswordResetTokenRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task CreateAsync(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                Guid id = Guid.NewGuid();

                string query = $@"
                    INSERT INTO password_reset_token (id, user_id, token_hash, created_at, expires_at)
                    VALUES (@{nameof(id)}, @{nameof(userId)}, @{nameof(tokenHash)}, @{nameof(createdAt)}, @{nameof(expiresAt)})";

                await dbSession.Connection.ExecuteAsync(
                    query, new { id, userId, tokenHash, createdAt, expiresAt }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<PasswordResetTokenRecord?> GetByTokenHashAsync(string tokenHash, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT
                        id,
                        user_id AS {nameof(PasswordResetTokenEntity.UserId)},
                        token_hash AS {nameof(PasswordResetTokenEntity.TokenHash)},
                        created_at AS {nameof(PasswordResetTokenEntity.CreatedAt)},
                        expires_at AS {nameof(PasswordResetTokenEntity.ExpiresAt)},
                        used_at AS {nameof(PasswordResetTokenEntity.UsedAt)}
                    FROM password_reset_token
                    WHERE token_hash = @{nameof(tokenHash)}";

                PasswordResetTokenEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<PasswordResetTokenEntity>(
                    query, new { tokenHash }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> MarkAsUsedAsync(Guid tokenId, DateTimeOffset usedAt, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE password_reset_token
                    SET used_at = @{nameof(usedAt)}
                    WHERE id = @{nameof(tokenId)}
                      AND used_at IS NULL";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query, new { tokenId, usedAt }, transaction: dbSession.Transaction);

                return rowsAffected > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset invalidatedAt, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE password_reset_token
                    SET used_at = @{nameof(invalidatedAt)}
                    WHERE user_id = @{nameof(userId)}
                      AND used_at IS NULL";

                await dbSession.Connection.ExecuteAsync(
                    query, new { userId, invalidatedAt }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredAsync(DateTimeOffset expiredBefore, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    DELETE FROM password_reset_token
                    WHERE expires_at < @{nameof(expiredBefore)}";

                return await dbSession.Connection.ExecuteAsync(
                    query, new { expiredBefore }, transaction: dbSession.Transaction);
            }, session);
        }

        internal class PasswordResetTokenEntity
        {
            public Guid Id { get; set; }
            public Guid UserId { get; set; }
            public string TokenHash { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public DateTimeOffset? UsedAt { get; set; }

            public PasswordResetTokenRecord ToDomain()
            {
                return new PasswordResetTokenRecord
                {
                    Id = Id,
                    UserId = UserId,
                    TokenHash = TokenHash,
                    CreatedAt = CreatedAt,
                    ExpiresAt = ExpiresAt,
                    UsedAt = UsedAt,
                };
            }
        }
    }
}
