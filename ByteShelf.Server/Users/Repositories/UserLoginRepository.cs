using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Users.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// SQLite implementation of <see cref="IUserLoginRepository"/> for the <c>user_login</c> table.
    /// The password hash lives on the <c>provider = 'local'</c> row. Every write logs the row as it is
    /// afterwards, credential included, in the caller's transaction.
    /// </summary>
    public class UserLoginRepository : RepositoryBase, IUserLoginRepository
    {
        /// <summary>
        /// The provider value for email + password logins. Additional providers (OAuth etc.) add
        /// rows with their own provider values — no identity migration.
        /// </summary>
        public const string LocalProvider = "local";

        private const string SelectColumns = $@"
            id,
            user_id AS {nameof(UserLoginEntity.UserId)},
            provider,
            provider_user_id AS {nameof(UserLoginEntity.ProviderUserId)},
            credential,
            created_at AS {nameof(UserLoginEntity.CreatedAt)}";

        private const string InsertSql = @"
            INSERT INTO user_login (id, user_id, provider, provider_user_id, credential, created_at)
            VALUES (@Id, @UserId, @Provider, @ProviderUserId, @Credential, @CreatedAt)";

        /// <summary>A user with their local login's password hash; callers add the condition naming whose.</summary>
        private const string SelectCredentials = $@"
            SELECT
                u.id,
                u.email,
                u.display_name AS {nameof(CredentialsEntity.DisplayName)},
                u.roles AS {nameof(CredentialsEntity.RolesJson)},
                u.created_at AS {nameof(CredentialsEntity.CreatedAt)},
                u.updated_at AS {nameof(CredentialsEntity.UpdatedAt)},
                ul.credential AS {nameof(CredentialsEntity.PasswordHash)}
            FROM user_login ul
            JOIN user_account u ON u.id = ul.user_id
            WHERE ul.provider = '{LocalProvider}'";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        private readonly IChangeLogRepository _changeLog;

        public UserLoginRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<UserCredentials?> GetCredentialsByEmailAsync(string email, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"{SelectCredentials} AND LOWER(ul.provider_user_id) = LOWER(@{nameof(email)})";

                CredentialsEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<CredentialsEntity>(
                    query, new { email }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<UserCredentials?> GetCredentialsByUserIdAsync(Guid userId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"{SelectCredentials} AND ul.user_id = @{nameof(userId)}";

                CredentialsEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<CredentialsEntity>(
                    query, new { userId }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task CreateLoginAsync(Guid userId, string provider, string providerUserId, string? credential, DateTimeOffset createdAt, IDbSession session)
        {
            UserLogin login = new UserLogin
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = provider,
                ProviderUserId = providerUserId,
                Credential = credential,
                CreatedAt = createdAt,
            };

            await session.Connection.ExecuteAsync(InsertSql, Parameters(login), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.UserLogin, login.Id, ChangeAction.Added, login, createdAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCredentialAsync(Guid userId, string provider, string newCredential, DateTimeOffset updatedAt, IDbSession session)
        {
            string query = $@"
                UPDATE user_login
                SET credential = @{nameof(newCredential)}
                WHERE user_id = @{nameof(userId)} AND provider = @{nameof(provider)}
                RETURNING {SelectColumns}";

            UserLoginEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<UserLoginEntity>(
                query, new { userId, provider, newCredential }, transaction: session.Transaction);

            if (updated == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.UserLogin, updated.Id, ChangeAction.Updated, updated.ToDomain(), updatedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task DeleteForUserAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM user_login WHERE user_id = @{nameof(userId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.UserLogin, query, new { userId }, deletedAt, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(UserLogin login, string action, DateTimeOffset at, IDbSession session)
        {
            string asideQuery = @"
                DELETE FROM user_login
                WHERE id <> @Id AND ((provider = @Provider AND provider_user_id = @ProviderUserId) OR (user_id = @UserId AND provider = @Provider))
                RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.UserLogin, asideQuery, new { login.Id, login.UserId, login.Provider, login.ProviderUserId }, at, session);

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(login), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.UserLogin, login.Id, action, login, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM user_login WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.UserLogin, query, new { id }, deletedAt, session) > 0;
        }

        private static object Parameters(UserLogin login)
        {
            return new { login.Id, login.UserId, login.Provider, login.ProviderUserId, login.Credential, login.CreatedAt };
        }

        internal class UserLoginEntity
        {
            public Guid Id { get; set; }
            public Guid UserId { get; set; }
            public string Provider { get; set; } = string.Empty;
            public string ProviderUserId { get; set; } = string.Empty;
            public string? Credential { get; set; }
            public DateTimeOffset CreatedAt { get; set; }

            public UserLogin ToDomain()
            {
                return new UserLogin
                {
                    Id = Id,
                    UserId = UserId,
                    Provider = Provider,
                    ProviderUserId = ProviderUserId,
                    Credential = Credential,
                    CreatedAt = CreatedAt,
                };
            }
        }

        internal class CredentialsEntity
        {
            public Guid Id { get; set; }
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string RolesJson { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public string PasswordHash { get; set; } = string.Empty;

            public UserCredentials ToDomain()
            {
                return new UserCredentials
                {
                    User = new User
                    {
                        Id = Id,
                        Email = Email,
                        DisplayName = DisplayName,
                        Roles = RolesJsonParser.Parse(RolesJson),
                        CreatedAt = CreatedAt,
                        UpdatedAt = UpdatedAt,
                    },
                    PasswordHash = PasswordHash,
                };
            }
        }
    }
}
