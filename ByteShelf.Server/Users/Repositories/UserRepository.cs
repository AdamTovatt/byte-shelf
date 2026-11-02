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
    /// SQLite implementation of <see cref="IUserRepository"/> for the <c>user_account</c> table. Every
    /// write logs the user as it is afterwards, never their credentials, in the caller's transaction.
    /// </summary>
    public class UserRepository : RepositoryBase, IUserRepository
    {
        private const string SelectColumns = $@"
            id,
            email,
            display_name AS {nameof(UserEntity.DisplayName)},
            roles AS {nameof(UserEntity.RolesJson)},
            created_at AS {nameof(UserEntity.CreatedAt)},
            updated_at AS {nameof(UserEntity.UpdatedAt)},
            sessions_ended_at AS {nameof(UserEntity.SessionsEndedAt)}";

        private const string InsertSql = @"
            INSERT INTO user_account (id, email, display_name, roles, created_at, updated_at, sessions_ended_at)
            VALUES (@Id, @Email, @DisplayName, @Roles, @CreatedAt, @UpdatedAt, @SessionsEndedAt)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        /// <summary>What a user's email becomes, after their id, while a replicated user takes it over.</summary>
        private const string SetAsideEmailDomain = "@set-aside.invalid";

        private readonly IChangeLogRepository _changeLog;

        public UserRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
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
        public async Task InsertAsync(User user, IDbSession session)
        {
            await session.Connection.ExecuteAsync(InsertSql, Parameters(user), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.User, user.Id, ChangeAction.Added, user, user.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateRolesAsync(Guid userId, IReadOnlyList<string> roles, DateTimeOffset updatedAt, IDbSession session)
        {
            string rolesJson = RolesJsonParser.Serialize(roles);

            string query = $@"
                UPDATE user_account
                SET roles = @{nameof(rolesJson)}, updated_at = @{nameof(updatedAt)}
                WHERE id = @{nameof(userId)}
                RETURNING {SelectColumns}";

            UserEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<UserEntity>(
                query, new { userId, rolesJson, updatedAt }, transaction: session.Transaction);

            if (updated == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.User, userId, ChangeAction.Updated, updated.ToDomain(), updatedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> EndSessionsAsync(Guid userId, DateTimeOffset at, IDbSession session)
        {
            string query = $@"
                UPDATE user_account
                SET sessions_ended_at = @{nameof(at)}, updated_at = @{nameof(at)}
                WHERE id = @{nameof(userId)}
                RETURNING {SelectColumns}";

            UserEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<UserEntity>(
                query, new { userId, at }, transaction: session.Transaction);

            if (updated == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.User, userId, ChangeAction.Updated, updated.ToDomain(), at, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM user_account WHERE id = @{nameof(userId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.User, query, new { userId }, deletedAt, session) > 0;
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(User user, string action, DateTimeOffset at, IDbSession session)
        {
            // Set aside, not removed: the other user keeps their logins and grants until the log says
            // what became of them.
            string asideQuery = $@"
                UPDATE user_account
                SET email = id || '{SetAsideEmailDomain}', updated_at = @{nameof(at)}
                WHERE LOWER(email) = LOWER(@Email) AND id <> @Id
                RETURNING {SelectColumns}";

            IEnumerable<UserEntity> setAside = await session.Connection.QueryAsync<UserEntity>(
                asideQuery, new { user.Email, user.Id, at }, transaction: session.Transaction);

            foreach (UserEntity aside in setAside)
            {
                await _changeLog.AppendAsync(ChangeEntityType.User, aside.Id, ChangeAction.Updated, aside.ToDomain(), at, session);
            }

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(user), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.User, user.Id, action, user, at, session);
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

        private static object Parameters(User user)
        {
            return new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                Roles = RolesJsonParser.Serialize(user.Roles),
                user.CreatedAt,
                user.UpdatedAt,
                user.SessionsEndedAt,
            };
        }

        internal class UserEntity
        {
            public Guid Id { get; set; }
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string RolesJson { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public DateTimeOffset? SessionsEndedAt { get; set; }

            public User ToDomain()
            {
                return new User
                {
                    Id = Id,
                    Email = Email,
                    DisplayName = DisplayName,
                    Roles = RolesJsonParser.Parse(RolesJson),
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                    SessionsEndedAt = SessionsEndedAt,
                };
            }
        }
    }
}
