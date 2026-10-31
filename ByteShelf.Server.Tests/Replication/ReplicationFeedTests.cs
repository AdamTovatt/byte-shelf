using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>The pages a primary serves: whether more follow, and when a replica must copy instead.</summary>
    public class ReplicationFeedTests : ServiceTestBase
    {
        private static readonly Replica Replica = new Replica { Id = Guid.NewGuid(), Name = "backup", PairedAt = TestInstants.Now, AppliedSequence = 0 };

        private IReplicationFeed Feed => Services.GetRequiredService<IReplicationFeed>();

        [Fact]
        public async Task GetChangesAsync_MoreThanAPage_SaysMoreFollowUntilTheLastPage()
        {
            await CreateShelfAsync();
            long latest = await ChangeLog.GetLatestSequenceAsync();

            ChangeFeedPage first = await Feed.GetChangesAsync(Replica, 0, null, limit: 1);
            ChangeFeedPage last = await Feed.GetChangesAsync(Replica, latest - 1, null, limit: 1);

            Assert.Equal((1, true), (first.Entries.Count, first.HasMore));
            Assert.Equal((1, false), (last.Entries.Count, last.HasMore));
        }

        [Fact]
        public async Task GetCopyAsync_MoreThanAPage_SaysMoreFollow()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");

            ChangeFeedPage first = await Feed.GetCopyAsync(0, limit: 1);
            ChangeFeedPage all = await Feed.GetCopyAsync(0, limit: 100);

            Assert.True(first.HasMore);
            Assert.False(all.HasMore);
        }

        [Fact]
        public async Task GetChangesAsync_ReplicaAheadOfTheLog_IsToldToCopy()
        {
            await CreateShelfAsync();
            long latest = await ChangeLog.GetLatestSequenceAsync();

            ChangeFeedPage page = await Feed.GetChangesAsync(Replica, latest + 1, null, limit: 100);

            Assert.True(page.CopyNeeded);
            Assert.Empty(page.Entries);
        }

        [Fact]
        public async Task GetChangesAsync_TheEntryTheReplicaAppliedLast_LetsItFollow()
        {
            await CreateShelfAsync();
            ChangeLogEntry last = (await ChangeLog.GetAfterAsync(0, 100))[^1];

            ChangeFeedPage page = await Feed.GetChangesAsync(Replica, last.Sequence, last.GetFingerprint(), limit: 100);

            Assert.False(page.CopyNeeded);
        }

        [Fact]
        public async Task GetChangesAsync_AnotherEntryUnderTheNumberTheReplicaApplied_IsToldToCopy()
        {
            await CreateShelfAsync();
            ChangeLogEntry last = (await ChangeLog.GetAfterAsync(0, 100))[^1];
            ChangeLogEntry other = new ChangeLogEntry
            {
                Sequence = last.Sequence,
                EntityType = last.EntityType,
                EntityId = Guid.NewGuid(),
                Action = last.Action,
                CreatedAt = last.CreatedAt,
            };

            ChangeFeedPage page = await Feed.GetChangesAsync(Replica, last.Sequence, other.GetFingerprint(), limit: 100);

            Assert.True(page.CopyNeeded);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(AppBaseUrl);
        }
    }
}
