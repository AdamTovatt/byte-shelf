using System.Data.Common;
using EasyReasy.Auth;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// SQLite-backed implementation of <see cref="IRefreshTokenStore"/> for persisting refresh
    /// tokens. Registered by <c>AddRefreshTokenService</c>; the base policy is multi-session (one
    /// token family per device), so nothing here revokes other sessions on login.
    /// </summary>
    public class RefreshTokenStore : RepositoryBase, IRefreshTokenStore, IExpiredRefreshTokenCleanup, ISessionStampStore
    {
        public RefreshTokenStore(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task StoreAsync(StoredRefreshToken refreshToken, CancellationToken cancellationToken = default)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string tokenHash = refreshToken.TokenHash;
                string subject = refreshToken.Subject;
                string authType = refreshToken.AuthType;
                string familyId = refreshToken.FamilyId;
                DateTime createdAt = refreshToken.CreatedAt.ToUniversalTime();
                DateTime expiresAt = refreshToken.ExpiresAt.ToUniversalTime();
                DateTime? consumedAt = refreshToken.ConsumedAt?.ToUniversalTime();
                bool isInvalidated = refreshToken.IsInvalidated;
                string? serializedClaims = refreshToken.SerializedClaims;
                string? serializedRoles = refreshToken.SerializedRoles;

                // A token joining a family that has been invalidated is stored invalidated. Otherwise a
                // refresh that read its token just before a password reset invalidated the family would
                // store a working successor afterwards and survive the reset.
                string query = $@"
                    INSERT INTO refresh_token (
                        token_hash, subject, auth_type, family_id,
                        created_at, expires_at, consumed_at, is_invalidated,
                        serialized_claims, serialized_roles
                    ) VALUES (
                        @{nameof(tokenHash)}, @{nameof(subject)}, @{nameof(authType)}, @{nameof(familyId)},
                        @{nameof(createdAt)}, @{nameof(expiresAt)}, @{nameof(consumedAt)},
                        @{nameof(isInvalidated)} OR EXISTS (
                            SELECT 1 FROM refresh_token WHERE family_id = @{nameof(familyId)} AND is_invalidated = 1),
                        @{nameof(serializedClaims)}, @{nameof(serializedRoles)}
                    )";

                await dbSession.Connection.ExecuteAsync(
                    query,
                    new { tokenHash, subject, authType, familyId, createdAt, expiresAt, consumedAt, isInvalidated, serializedClaims, serializedRoles },
                    transaction: dbSession.Transaction);
            });
        }

        /// <inheritdoc />
        public async Task<StoredRefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT
                        token_hash AS {nameof(RefreshTokenEntity.TokenHash)},
                        subject AS {nameof(RefreshTokenEntity.Subject)},
                        auth_type AS {nameof(RefreshTokenEntity.AuthType)},
                        family_id AS {nameof(RefreshTokenEntity.FamilyId)},
                        created_at AS {nameof(RefreshTokenEntity.CreatedAt)},
                        expires_at AS {nameof(RefreshTokenEntity.ExpiresAt)},
                        consumed_at AS {nameof(RefreshTokenEntity.ConsumedAt)},
                        is_invalidated AS {nameof(RefreshTokenEntity.IsInvalidated)},
                        serialized_claims AS {nameof(RefreshTokenEntity.SerializedClaims)},
                        serialized_roles AS {nameof(RefreshTokenEntity.SerializedRoles)}
                    FROM refresh_token
                    WHERE token_hash = @{nameof(tokenHash)}";

                RefreshTokenEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<RefreshTokenEntity>(
                    query, new { tokenHash }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            });
        }

        /// <inheritdoc />
        public async Task<bool> MarkAsConsumedAsync(string tokenHash, DateTime consumedAt, CancellationToken cancellationToken = default)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                DateTime consumedAtUtc = consumedAt.ToUniversalTime();

                string query = $@"
                    UPDATE refresh_token
                    SET consumed_at = @{nameof(consumedAtUtc)}
                    WHERE token_hash = @{nameof(tokenHash)} AND consumed_at IS NULL";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query, new { tokenHash, consumedAtUtc }, transaction: dbSession.Transaction);

                return rowsAffected > 0;
            });
        }

        /// <inheritdoc />
        public async Task InvalidateFamilyAsync(string familyId, CancellationToken cancellationToken = default)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE refresh_token
                    SET is_invalidated = 1
                    WHERE family_id = @{nameof(familyId)}";

                await dbSession.Connection.ExecuteAsync(
                    query, new { familyId }, transaction: dbSession.Transaction);
            });
        }

        /// <inheritdoc />
        public async Task<int> InvalidateAllFamiliesForUserAsync(string subject, CancellationToken cancellationToken = default)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // SQLite allows RETURNING only on the outermost statement, so the distinct families are
                // counted from the returned rows rather than in a wrapping CTE.
                string query = $@"
                    UPDATE refresh_token
                    SET is_invalidated = 1
                    WHERE subject = @{nameof(subject)}
                      AND is_invalidated = 0
                    RETURNING family_id AS {nameof(FamilyIdEntity.FamilyId)}";

                IEnumerable<FamilyIdEntity> families = await dbSession.Connection.QueryAsync<FamilyIdEntity>(
                    query, new { subject }, transaction: dbSession.Transaction);

                return families.Select(family => family.FamilyId).Distinct().Count();
            });
        }

        /// <inheritdoc />
        public async Task RestampFamilyAsync(string subject, string familyId, string serializedClaims, IDbSession session)
        {
            string query = $@"
                UPDATE refresh_token
                SET serialized_claims = @{nameof(serializedClaims)}
                WHERE family_id = @{nameof(familyId)}
                  AND subject = @{nameof(subject)}
                  AND consumed_at IS NULL
                  AND is_invalidated = 0";

            await session.Connection.ExecuteAsync(query, new { subject, familyId, serializedClaims }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredTokensAsync(DateTimeOffset expiredBefore)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    DELETE FROM refresh_token
                    WHERE expires_at < @{nameof(expiredBefore)}";

                return await dbSession.Connection.ExecuteAsync(
                    query, new { expiredBefore }, transaction: dbSession.Transaction);
            });
        }

        internal class FamilyIdEntity
        {
            public string FamilyId { get; set; } = string.Empty;
        }

        internal class RefreshTokenEntity
        {
            public string TokenHash { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string AuthType { get; set; } = string.Empty;
            public string FamilyId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime? ConsumedAt { get; set; }
            public bool IsInvalidated { get; set; }
            public string? SerializedClaims { get; set; }
            public string? SerializedRoles { get; set; }

            public StoredRefreshToken ToDomain()
            {
                return new StoredRefreshToken
                {
                    TokenHash = TokenHash,
                    Subject = Subject,
                    AuthType = AuthType,
                    FamilyId = FamilyId,
                    CreatedAt = CreatedAt,
                    ExpiresAt = ExpiresAt,
                    ConsumedAt = ConsumedAt,
                    IsInvalidated = IsInvalidated,
                    SerializedClaims = SerializedClaims,
                    SerializedRoles = SerializedRoles,
                };
            }
        }
    }
}
