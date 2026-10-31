using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
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
                string query = $@"
                    SELECT
                        u.id,
                        u.email,
                        u.display_name AS {nameof(CredentialsEntity.DisplayName)},
                        u.roles AS {nameof(CredentialsEntity.RolesJson)},
                        ul.credential AS {nameof(CredentialsEntity.PasswordHash)}
                    FROM user_login ul
                    JOIN user_account u ON u.id = ul.user_id
                    WHERE ul.provider = '{LocalProvider}' AND LOWER(ul.provider_user_id) = LOWER(@{nameof(email)})";

                CredentialsEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<CredentialsEntity>(
                    query, new { email }, transaction: dbSession.Transaction);

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

            string query = @"
                INSERT INTO user_login (id, user_id, provider, provider_user_id, credential, created_at)
                VALUES (@Id, @UserId, @Provider, @ProviderUserId, @Credential, @CreatedAt)";

            await session.Connection.ExecuteAsync(
                query,
                new { login.Id, login.UserId, login.Provider, login.ProviderUserId, login.Credential, login.CreatedAt },
                transaction: session.Transaction);

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
                    },
                    PasswordHash = PasswordHash,
                };
            }
        }
    }
}
