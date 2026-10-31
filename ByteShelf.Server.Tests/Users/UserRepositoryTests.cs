using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Tests.Users
{
    public class UserRepositoryTests : TransactionalRepositoryTestBase
    {
        private UserRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = new UserRepository(DataSource, SessionFactory, new ChangeLogRepository(DataSource, SessionFactory));
        }

        [Fact]
        public async Task InsertAsync_WithRoles_RoundtripsThroughGetById()
        {
            User user = NewUser("roundtrip@example.com", UserRoles.Admin);

            await _repository.InsertAsync(user, TestInstants.Now, Session);
            User? fetched = await _repository.GetByIdAsync(user.Id, Session);

            Assert.NotNull(fetched);
            Assert.Equal("roundtrip@example.com", fetched.Email);
            Assert.Equal("Name", fetched.DisplayName);
            Assert.Equal(new[] { UserRoles.Admin }, fetched.Roles);
        }

        [Fact]
        public async Task InsertAsync_DuplicateEmailDifferentCase_ThrowsUniqueViolation()
        {
            await _repository.InsertAsync(NewUser("casetest@example.com"), TestInstants.Now, Session);

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => _repository.InsertAsync(NewUser("CaseTest@Example.com"), TestInstants.Now, Session));

            Assert.True(SqliteUniqueViolation.Is(exception));
        }

        [Fact]
        public async Task UpdateRolesAsync_ExistingUser_ReplacesRoles()
        {
            User user = NewUser("roles@example.com");
            await _repository.InsertAsync(user, TestInstants.Now, Session);

            bool updated = await _repository.UpdateRolesAsync(user.Id, new[] { UserRoles.Admin }, TestInstants.Now, Session);

            Assert.True(updated);
            Assert.Equal(new[] { UserRoles.Admin }, (await _repository.GetByIdAsync(user.Id, Session))!.Roles);
        }

        [Fact]
        public async Task CountWithRoleAsync_CountsOnlyHoldersOfTheRole()
        {
            await _repository.InsertAsync(NewUser("admin@example.com", UserRoles.Admin), TestInstants.Now, Session);
            await _repository.InsertAsync(NewUser("plain@example.com"), TestInstants.Now, Session);

            Assert.Equal(1, await _repository.CountWithRoleAsync(UserRoles.Admin, Session));
            Assert.Equal(2, await _repository.GetCountAsync(Session));
        }

        [Fact]
        public async Task GetByIdAsync_RolesColumnDamaged_Throws()
        {
            User user = NewUser("damaged@example.com");
            await _repository.InsertAsync(user, TestInstants.Now, Session);
            await ExecuteAsync(Session, "UPDATE user_account SET roles = 'not json'");

            await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.GetByIdAsync(user.Id, Session));
        }

        private static User NewUser(string email, params string[] roles)
        {
            return new User { Id = Guid.NewGuid(), Email = email, DisplayName = "Name", Roles = roles };
        }

        private static async Task ExecuteAsync(IDbSession session, string sql)
        {
            using System.Data.Common.DbCommand command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
