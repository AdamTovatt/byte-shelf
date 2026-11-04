using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Users
{
    public class UserLoginRepositoryTests : TransactionalRepositoryTestBase
    {
        private UserRepository _userRepository = null!;
        private UserLoginRepository _loginRepository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _userRepository = new UserRepository(DataSource, SessionFactory, new ChangeLogRepository(DataSource, SessionFactory));
            _loginRepository = new UserLoginRepository(DataSource, SessionFactory, new ChangeLogRepository(DataSource, SessionFactory));
        }

        [Fact]
        public async Task GetCredentialsByEmailAsync_LocalLogin_ReturnsTheUserWithTheHash()
        {
            User user = await CreateUserWithLoginAsync("creds@example.com", "hashed-password", UserRoles.Admin);

            UserCredentials? credentials = await _loginRepository.GetCredentialsByEmailAsync("creds@example.com", Session);

            Assert.NotNull(credentials);
            Assert.Equal(user.Id, credentials.User.Id);
            Assert.Equal("creds@example.com", credentials.User.Email);
            Assert.Equal("Name", credentials.User.DisplayName);
            Assert.Equal(new[] { UserRoles.Admin }, credentials.User.Roles);
            Assert.Equal("hashed-password", credentials.PasswordHash);
        }

        [Fact]
        public async Task GetCredentialsByEmailAsync_DifferentCase_StillMatches()
        {
            User user = await CreateUserWithLoginAsync("mixed@example.com", "hash");

            UserCredentials? credentials = await _loginRepository.GetCredentialsByEmailAsync("Mixed@Example.COM", Session);

            Assert.Equal(user.Id, credentials?.User.Id);
        }

        [Fact]
        public async Task GetCredentialsByEmailAsync_UnknownEmail_ReturnsNull()
        {
            Assert.Null(await _loginRepository.GetCredentialsByEmailAsync("nobody@example.com", Session));
        }

        [Fact]
        public async Task UpdateCredentialAsync_ExistingLogin_ReplacesHash()
        {
            User user = await CreateUserWithLoginAsync("rotate@example.com", "old-hash");

            bool updated = await _loginRepository.UpdateCredentialAsync(user.Id, UserLoginRepository.LocalProvider, "new-hash", TestInstants.Now, Session);

            Assert.True(updated);
            Assert.Equal("new-hash", (await _loginRepository.GetCredentialsByEmailAsync("rotate@example.com", Session))?.PasswordHash);
        }

        private async Task<User> CreateUserWithLoginAsync(string email, string passwordHash, params string[] roles)
        {
            User user = new User { Id = Guid.NewGuid(), Email = email, DisplayName = "Name", Roles = roles, CreatedAt = TestInstants.Now, UpdatedAt = TestInstants.Now };
            await _userRepository.InsertAsync(user, Session);
            await _loginRepository.CreateLoginAsync(user.Id, UserLoginRepository.LocalProvider, email, passwordHash, TestInstants.Now, Session);
            return user;
        }
    }
}
