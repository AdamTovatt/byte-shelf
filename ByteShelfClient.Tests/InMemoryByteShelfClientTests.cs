using ByteShelfClient.InMemory;
using ByteShelfCommon;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelfClient.Tests
{
    /// <summary>What only the in-memory client does; the rules it shares with the server are in the server's contract tests.</summary>
    public class InMemoryByteShelfClientTests
    {
        [Fact]
        public async Task Trash_PastTheRestoreWindow_IsPurged()
        {
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            InMemoryByteShelfClient client = new InMemoryByteShelfClient(timeProvider: clock, restoreWindow: TimeSpan.FromDays(30));
            await client.CreateShelfAsync("docs");
            IShelfClient shelf = client.Shelf("docs");
            await shelf.UploadAsync("/a.txt", new MemoryStream(new byte[] { 1, 2, 3 }));
            TrashEntry deleted = await shelf.DeleteFileAsync("/a.txt");

            clock.Advance(TimeSpan.FromDays(30));
            Assert.Single(await shelf.GetTrashAsync());

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(await shelf.GetTrashAsync());
            Assert.Equal(0, (await shelf.GetInfoAsync()).UsedBytes);
            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => shelf.RestoreAsync(deleted.DeletionId));
            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        /// <summary>A replica refuses a write before it looks for the shelf, as the server's read-only filter runs before the action.</summary>
        [Fact]
        public async Task ShelfWrite_OnAFollower_IsRefusedAsAReplica()
        {
            InMemoryByteShelfClient primary = new InMemoryByteShelfClient();
            InMemoryByteShelfClient follower = new InMemoryByteShelfClient();
            await follower.FollowPrimaryAsync(new PairWithPrimaryRequest { Code = (await primary.CreatePairingCodeAsync()).Code, Name = "backup" });

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => follower.Shelf("docs").UploadAsync("/a.txt", new MemoryStream(new byte[] { 1 })));

            Assert.Equal(ErrorCodes.ReadOnlyReplica, exception.Code);
        }

        [Fact]
        public async Task FollowPrimary_ACodePastItsLifetime_IsRefused()
        {
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            InMemoryByteShelfClient primary = new InMemoryByteShelfClient(timeProvider: clock);
            PairingCodeResponse code = await primary.CreatePairingCodeAsync();
            Assert.Equal(clock.GetUtcNow() + PairingCodeResponse.Lifetime, code.ExpiresAt);

            clock.Advance(PairingCodeResponse.Lifetime);
            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => new InMemoryByteShelfClient().FollowPrimaryAsync(new PairWithPrimaryRequest { Code = code.Code, Name = "backup" }));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
            Assert.Empty((await primary.GetReplicationAsync()).Replicas);
        }

        [Fact]
        public async Task GetLinks_LinkThatRanOut_IsLeftOut()
        {
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            InMemoryByteShelfClient client = new InMemoryByteShelfClient(timeProvider: clock);
            await client.CreateShelfAsync("docs");
            IShelfClient shelf = client.Shelf("docs");
            UploadLink link = await shelf.CreateUploadLinkAsync("/a.txt", new UploadLinkOptions { Lifetime = TimeSpan.FromMinutes(5) });

            clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
            Assert.Equal(link.Id, Assert.Single(await shelf.GetLinksAsync()).Id);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(await shelf.GetLinksAsync());
        }

        [Fact]
        public async Task GetLinks_MakerDeleted_LeavesTheirLinksOut()
        {
            InMemoryByteShelfClient client = new InMemoryByteShelfClient();
            await client.CreateShelfAsync("docs");
            await client.Shelf("docs").CreateUploadLinkAsync("/a.txt");
            await client.CreateUserAsync(new CreateUserRequest { Email = "other@byteshelf.test", DisplayName = "Other", Password = "password-for-tests", Roles = new[] { UserRoles.Admin } });

            await client.DeleteUserAsync(client.CurrentUser.Id);

            Assert.Empty(await client.Shelf("docs").GetLinksAsync());
        }
    }
}
