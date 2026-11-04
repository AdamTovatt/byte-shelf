using EasyReasy.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Repositories;

namespace ByteShelf.Server.Tests.Users
{
    /// <summary>
    /// The <see cref="IRefreshTokenStore"/> contract has no session parameter, so its rows commit; each
    /// test has a database of its own, so none sees another's.
    /// </summary>
    public class RefreshTokenStoreTests : RepositoryTestBase
    {
        private RefreshTokenStore _store = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _store = new RefreshTokenStore(DataSource, SessionFactory);
        }

        private static StoredRefreshToken NewToken(string subject, string familyId, string tokenHash, DateTime? expiresAt = null)
        {
            DateTime now = TestInstants.Now.UtcDateTime;

            return new StoredRefreshToken
            {
                TokenHash = tokenHash,
                Subject = subject,
                AuthType = "user",
                FamilyId = familyId,
                CreatedAt = now,
                ExpiresAt = expiresAt ?? now.AddDays(30),
                ConsumedAt = null,
                IsInvalidated = false,
                SerializedClaims = null,
                SerializedRoles = "[\"write\"]",
            };
        }

        [Fact]
        public async Task StoreAsync_ThenGetByTokenHash_RoundtripsAllFields()
        {
            string tokenHash = Guid.NewGuid().ToString("N");
            string subject = Guid.NewGuid().ToString();
            StoredRefreshToken token = NewToken(subject, "family-roundtrip", tokenHash);

            await _store.StoreAsync(token);
            StoredRefreshToken? fetched = await _store.GetByTokenHashAsync(tokenHash);

            Assert.NotNull(fetched);
            Assert.Equal(tokenHash, fetched.TokenHash);
            Assert.Equal(subject, fetched.Subject);
            Assert.Equal("user", fetched.AuthType);
            Assert.Equal("family-roundtrip", fetched.FamilyId);
            Assert.Equal(token.CreatedAt, fetched.CreatedAt.ToUniversalTime());
            Assert.Equal(token.ExpiresAt, fetched.ExpiresAt.ToUniversalTime());
            Assert.Null(fetched.ConsumedAt);
            Assert.False(fetched.IsInvalidated);
            Assert.Null(fetched.SerializedClaims);
            Assert.Equal("[\"write\"]", fetched.SerializedRoles);
        }

        [Fact]
        public async Task MarkAsConsumedAsync_SecondCall_ReturnsFalse()
        {
            string tokenHash = Guid.NewGuid().ToString("N");
            await _store.StoreAsync(NewToken(Guid.NewGuid().ToString(), "family-consume", tokenHash));
            DateTime consumedAt = TestInstants.Now.UtcDateTime.AddDays(1);

            bool firstConsume = await _store.MarkAsConsumedAsync(tokenHash, consumedAt);
            bool secondConsume = await _store.MarkAsConsumedAsync(tokenHash, consumedAt);

            Assert.True(firstConsume);
            Assert.False(secondConsume);
        }

        [Fact]
        public async Task InvalidateFamilyAsync_MarksEveryTokenInFamily()
        {
            string subject = Guid.NewGuid().ToString();
            string familyId = $"family-{Guid.NewGuid():N}";
            string firstHash = Guid.NewGuid().ToString("N");
            string secondHash = Guid.NewGuid().ToString("N");
            await _store.StoreAsync(NewToken(subject, familyId, firstHash));
            await _store.StoreAsync(NewToken(subject, familyId, secondHash));

            await _store.InvalidateFamilyAsync(familyId);

            StoredRefreshToken? first = await _store.GetByTokenHashAsync(firstHash);
            StoredRefreshToken? second = await _store.GetByTokenHashAsync(secondHash);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.True(first.IsInvalidated);
            Assert.True(second.IsInvalidated);
        }

        [Fact]
        public async Task InvalidateAllFamiliesForUserAsync_CountsDistinctFamilies()
        {
            string subject = Guid.NewGuid().ToString();
            string familyA = $"family-{Guid.NewGuid():N}";
            string familyB = $"family-{Guid.NewGuid():N}";
            await _store.StoreAsync(NewToken(subject, familyA, Guid.NewGuid().ToString("N")));
            await _store.StoreAsync(NewToken(subject, familyA, Guid.NewGuid().ToString("N")));
            await _store.StoreAsync(NewToken(subject, familyB, Guid.NewGuid().ToString("N")));

            int revokedFamilies = await _store.InvalidateAllFamiliesForUserAsync(subject);

            Assert.Equal(2, revokedFamilies);
        }

        [Fact]
        public async Task DeleteExpiredTokensAsync_RemovesOnlyExpiredRows()
        {
            string subject = Guid.NewGuid().ToString();
            string expiredHash = Guid.NewGuid().ToString("N");
            string liveHash = Guid.NewGuid().ToString("N");
            DateTime cutoff = TestInstants.Now.UtcDateTime;
            await _store.StoreAsync(NewToken(subject, "family-expired", expiredHash, expiresAt: cutoff.AddDays(-1)));
            await _store.StoreAsync(NewToken(subject, "family-live", liveHash, expiresAt: cutoff.AddDays(30)));

            await _store.DeleteExpiredTokensAsync(TestInstants.Now);

            Assert.Null(await _store.GetByTokenHashAsync(expiredHash));
            Assert.NotNull(await _store.GetByTokenHashAsync(liveHash));
        }

        [Fact]
        public async Task StoreAsync_FamilyAlreadyInvalidated_StoresTheTokenInvalidated()
        {
            string subject = Guid.NewGuid().ToString();
            string familyId = $"family-{Guid.NewGuid():N}";
            await _store.StoreAsync(NewToken(subject, familyId, Guid.NewGuid().ToString("N")));
            await _store.InvalidateAllFamiliesForUserAsync(subject);
            string successorHash = Guid.NewGuid().ToString("N");

            // A refresh that read its token before the family was invalidated stores its successor after.
            await _store.StoreAsync(NewToken(subject, familyId, successorHash));

            Assert.True((await _store.GetByTokenHashAsync(successorHash))?.IsInvalidated);
        }
    }
}
