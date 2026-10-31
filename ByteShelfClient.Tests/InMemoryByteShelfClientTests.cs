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
    }
}
