using System.Data.Common;
using ByteShelf.Server.Users.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// SQLite implementation of <see cref="IUserLoginRepository"/> for the <c>user_login</c> table.
    /// The password hash lives on the <c>provider = 'local'</c> row.
    /// </summary>
    public class UserLoginRepository : RepositoryBase, IUserLoginRepository
    {
        /// <summary>
        /// The provider value for email + password logins. Additional providers (OAuth etc.) add
        /// rows with their own provider values — no identity migration.
        /// </summary>
        public const string LocalProvider = "local";

        public UserLoginRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
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
            Guid id = Guid.NewGuid();

            string query = $@"
                INSERT INTO user_login (id, user_id, provider, provider_user_id, credential, created_at)
                VALUES (@{nameof(id)}, @{nameof(userId)}, @{nameof(provider)}, @{nameof(providerUserId)}, @{nameof(credential)}, @{nameof(createdAt)})";

            await session.Connection.ExecuteAsync(
                query, new { id, userId, provider, providerUserId, credential, createdAt }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCredentialAsync(Guid userId, string provider, string newCredential, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE user_login
                    SET credential = @{nameof(newCredential)}
                    WHERE user_id = @{nameof(userId)} AND provider = @{nameof(provider)}";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query, new { userId, provider, newCredential }, transaction: dbSession.Transaction);

                return rowsAffected > 0;
            }, session);
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
