using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Services;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Users
{
    public class PasskeyChallengesTests
    {
        private readonly FakeTimeProvider _time = new FakeTimeProvider(TestInstants.Now);

        [Fact]
        public void Take_RanOut_AnswersNothing()
        {
            PasskeyChallenges challenges = new PasskeyChallenges(_time);
            string ceremonyId = challenges.Keep("challenge", userId: null);

            _time.Advance(PasskeyChallenges.Lifetime);

            Assert.Null(challenges.Take(ceremonyId, userId: null));
        }

        [Fact]
        public void Take_AnotherUsersCeremony_AnswersNothingAndSpendsIt()
        {
            PasskeyChallenges challenges = new PasskeyChallenges(_time);
            Guid owner = Guid.NewGuid();
            string ceremonyId = challenges.Keep("challenge", owner);

            Assert.Null(challenges.Take(ceremonyId, Guid.NewGuid()));
            Assert.Null(challenges.Take(ceremonyId, owner));
        }

        [Fact]
        public void Keep_PastCapacity_DropsTheOldest()
        {
            PasskeyChallenges challenges = new PasskeyChallenges(_time);
            string oldest = challenges.Keep("oldest", userId: null);
            string next = challenges.Keep("next", userId: null);

            for (int ceremony = 2; ceremony <= PasskeyChallenges.Capacity; ceremony++)
            {
                challenges.Keep("flood", userId: null);
            }

            Assert.Null(challenges.Take(oldest, userId: null));
            Assert.Equal("next", challenges.Take(next, userId: null));
        }
    }
}
