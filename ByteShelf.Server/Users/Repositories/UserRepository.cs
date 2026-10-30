using System.Data.Common;
using ByteShelf.Server.Users.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>SQLite implementation of <see cref="IUserRepository"/> for the <c>user_account</c> table.</summary>
    public class UserRepository : RepositoryBase, IUserRepository
    {
        private const string SelectColumns = $@"
            id,
            email,
            display_name AS {nameof(UserEntity.DisplayName)},
            roles AS {nameof(UserEntity.RolesJson)}";

        public UserRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<User?> GetByIdAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM user_account WHERE id = @{nameof(id)}";

                UserEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<UserEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<User>> GetAllAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM user_account ORDER BY created_at";

                IEnumerable<UserEntity> entities = await dbSession.Connection.QueryAsync<UserEntity>(
                    query, transaction: dbSession.Transaction);

                return (IReadOnlyList<User>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(User user, DateTimeOffset createdAt, IDbSession session)
        {
            string rolesJson = RolesJsonParser.Serialize(user.Roles);

            string query = $@"
                INSERT INTO user_account (id, email, display_name, roles, created_at, updated_at)
                VALUES (@Id, @Email, @DisplayName, @{nameof(rolesJson)}, @{nameof(createdAt)}, @{nameof(createdAt)})";

            await session.Connection.ExecuteAsync(
                query,
                new { user.Id, user.Email, user.DisplayName, rolesJson, createdAt },
                transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateRolesAsync(Guid userId, IReadOnlyList<string> roles, DateTimeOffset updatedAt, IDbSession session)
        {
            string rolesJson = RolesJsonParser.Serialize(roles);

            string query = $@"
                UPDATE user_account
                SET roles = @{nameof(rolesJson)}, updated_at = @{nameof(updatedAt)}
                WHERE id = @{nameof(userId)}";

            int rowsAffected = await session.Connection.ExecuteAsync(
                query, new { userId, rolesJson, updatedAt }, transaction: session.Transaction);

            return rowsAffected > 0;
        }

        /// <inheritdoc />
        public async Task<int> GetCountAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM user_account", transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> CountWithRoleAsync(string role, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT COUNT(*)
                    FROM user_account
                    WHERE EXISTS (SELECT 1 FROM json_each(user_account.roles) WHERE value = @{nameof(role)})";

                return await dbSession.Connection.QuerySingleAsync<int>(
                    query, new { role }, transaction: dbSession.Transaction);
            }, session);
        }

        internal class UserEntity
        {
            public Guid Id { get; set; }
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string RolesJson { get; set; } = string.Empty;

            public User ToDomain()
            {
                return new User
                {
                    Id = Id,
                    Email = Email,
                    DisplayName = DisplayName,
                    Roles = RolesJsonParser.Parse(RolesJson),
                };
            }
        }
    }
}
