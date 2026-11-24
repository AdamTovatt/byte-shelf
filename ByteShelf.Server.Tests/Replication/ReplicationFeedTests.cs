using System.Text;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
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

            ChangeFeedPage first = await Feed.GetCopyAsync(Replica, 0, limit: 1);
            ChangeFeedPage all = await Feed.GetCopyAsync(Replica, 0, limit: 100);

            Assert.True(first.HasMore);
            Assert.False(all.HasMore);
        }

        /// <summary>Pages are held whole on both servers, so how much they carry is bounded, not only how many entries.</summary>
        [Fact]
        public async Task GetChangesAndCopyAsync_EntriesOfALotTogether_ComeInPagesOfAtMost4MiBWithoutMissingOne()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            string document = $"{{\"t\":\"{new string('x', 60_000)}\"}}";
            for (int index = 0; index < 80; index++)
            {
                await Services.SetFileDocumentAsync(file.Id, document, $"plugin-{index}");
            }

            long latest = await ChangeLog.GetLatestSequenceAsync();
            List<long> changes = await ReadAllAsync(after => Feed.GetChangesAsync(Replica, after, null, limit: 1000));
            List<long> copied = await ReadAllAsync(after => Feed.GetCopyAsync(Replica, after, limit: 1000));

            Assert.Equal(Enumerable.Range(1, (int)latest).Select(sequence => (long)sequence), changes);
            Assert.Equal((await Services.GetLatestStatesAsync()).Select(entry => entry.Sequence), copied);
        }

        [Fact]
        public async Task GetPageAsync_OneEntryOverTheLimitAlone_IsAPageOfItsOwn()
        {
            await CreateShelfAsync();
            await CreateShelfAsync("another");

            ChangeLogPage page = await Services.InTransactionAsync((_, session) => ChangeLog.GetPageAsync(0, new ChangeLogPageLimit(100, 1), session));

            Assert.Equal((1, true), (page.Entries.Count, page.HasMore));
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

        /// <summary>The sequences of every page in order, each page checked to hold as much as one may and no more.</summary>
        private static async Task<List<long>> ReadAllAsync(Func<long, Task<ChangeFeedPage>> readPage)
        {
            const int MaxBytes = 4 * 1024 * 1024;
            List<long> sequences = new List<long>();
            ChangeFeedPage page;
            do
            {
                page = await readPage(sequences.Count == 0 ? 0 : sequences[^1]);
                int bytes = page.Entries.Sum(entry => Encoding.UTF8.GetByteCount(entry.Payload ?? string.Empty));
                Assert.InRange(bytes, page.HasMore ? MaxBytes - 70_000 : 0, MaxBytes);
                sequences.AddRange(page.Entries.Select(entry => entry.Sequence));
            }
            while (page.HasMore);

            return sequences;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddReplicationServices(new ReplicationOptions { PublicUrl = AppBaseUrl });
        }
    }
}
