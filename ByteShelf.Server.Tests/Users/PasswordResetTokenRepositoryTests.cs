using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;

namespace ByteShelf.Server.Tests.Users
{
    public class PasswordResetTokenRepositoryTests : TransactionalRepositoryTestBase
    {
        private UserRepository _userRepository = null!;
        private PasswordResetTokenRepository _tokenRepository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _userRepository = new UserRepository(DataSource, SessionFactory);
            _tokenRepository = new PasswordResetTokenRepository(DataSource, SessionFactory);
        }

        [Fact]
        public async Task CreateAsync_ThenGetByTokenHash_RoundtripsTheRecord()
        {
            Guid userId = await CreateUserAsync();
            string tokenHash = NewHash();

            await _tokenRepository.CreateAsync(userId, tokenHash, TestInstants.Now, TestInstants.Now.AddHours(1), Session);
            PasswordResetTokenRecord? record = await _tokenRepository.GetByTokenHashAsync(tokenHash, Session);

            Assert.NotNull(record);
            Assert.Equal(userId, record.UserId);
            Assert.Equal(tokenHash, record.TokenHash);
            Assert.Equal(TestInstants.Now, record.CreatedAt);
            Assert.Equal(TestInstants.Now.AddHours(1), record.ExpiresAt);
            Assert.Null(record.UsedAt);
        }

        [Fact]
        public async Task GetByTokenHashAsync_UnknownHash_ReturnsNull()
        {
            Assert.Null(await _tokenRepository.GetByTokenHashAsync(NewHash(), Session));
        }

        [Fact]
        public async Task MarkAsUsedAsync_SecondCall_ReturnsFalse()
        {
            Guid userId = await CreateUserAsync();
            string tokenHash = NewHash();
            await _tokenRepository.CreateAsync(userId, tokenHash, TestInstants.Now, TestInstants.Now.AddHours(1), Session);
            PasswordResetTokenRecord record = (await _tokenRepository.GetByTokenHashAsync(tokenHash, Session))!;

            bool firstUse = await _tokenRepository.MarkAsUsedAsync(record.Id, TestInstants.Now, Session);
            bool secondUse = await _tokenRepository.MarkAsUsedAsync(record.Id, TestInstants.Now.AddMinutes(1), Session);

            Assert.True(firstUse);
            Assert.False(secondUse);
        }

        [Fact]
        public async Task InvalidateAllForUserAsync_MarksOutstandingTokensUsed()
        {
            Guid userId = await CreateUserAsync();
            string outstandingHash = NewHash();
            await _tokenRepository.CreateAsync(userId, outstandingHash, TestInstants.Now, TestInstants.Now.AddHours(1), Session);

            await _tokenRepository.InvalidateAllForUserAsync(userId, TestInstants.Now, Session);

            Assert.Equal(TestInstants.Now, (await _tokenRepository.GetByTokenHashAsync(outstandingHash, Session))?.UsedAt);
        }

        [Fact]
        public async Task DeleteExpiredAsync_RemovesOnlyExpiredRows()
        {
            Guid userId = await CreateUserAsync();
            string expiredHash = NewHash();
            string liveHash = NewHash();
            await _tokenRepository.CreateAsync(userId, expiredHash, TestInstants.Now, TestInstants.Now.AddHours(-1), Session);
            await _tokenRepository.CreateAsync(userId, liveHash, TestInstants.Now, TestInstants.Now.AddHours(1), Session);

            int deleted = await _tokenRepository.DeleteExpiredAsync(TestInstants.Now, Session);

            Assert.Equal(1, deleted);
            Assert.Null(await _tokenRepository.GetByTokenHashAsync(expiredHash, Session));
            Assert.NotNull(await _tokenRepository.GetByTokenHashAsync(liveHash, Session));
        }

        private async Task<Guid> CreateUserAsync()
        {
            User user = new User { Id = Guid.NewGuid(), Email = "reset@example.com", DisplayName = "Reset", Roles = Array.Empty<string>() };
            await _userRepository.InsertAsync(user, TestInstants.Now, Session);
            return user.Id;
        }

        private static string NewHash()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
