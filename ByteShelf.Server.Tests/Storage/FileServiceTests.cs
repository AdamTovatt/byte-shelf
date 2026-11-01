using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    public class FileServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task GetFileAsync_ByIdFromAnotherShelf_ThrowsNotFound()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            ShelfFile file = await StoreTextAsync(other, "/secret.txt", "x");

            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(mine, EntryReference.ForId(file.Id)));
        }

        [Fact]
        public async Task MoveFileAsync_Rename_KeepsTheIdAndTheObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "contents");

            ShelfFile renamed = await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/b.txt");

            Assert.Equal(file.Id, renamed.Id);
            Assert.Equal("/b.txt", renamed.Path);
            Assert.Equal(file.Sha256, renamed.Sha256);
            Assert.True(ObjectStore.Exists(file.Sha256));
            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt")));
        }

        [Fact]
        public async Task MoveFileAsync_IntoMissingDirectory_CreatesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "contents");

            ShelfFile moved = await FileService.MoveFileAsync(shelf, EntryReference.ForPath("/a.txt"), "/archive/2026/a.txt");

            Assert.Equal("/archive/2026/a.txt", moved.Path);
        }

        [Theory]
        [InlineData("/taken.txt")]
        [InlineData("/dir")]
        public async Task MoveFileAsync_DestinationTaken_ThrowsConflict(string toPath)
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/taken.txt", "t");
            await StoreTextAsync(shelf, "/dir/inner.txt", "i");

            await Assert.ThrowsAsync<ConflictException>(() => FileService.MoveFileAsync(shelf, EntryReference.ForPath("/a.txt"), toPath));
        }

        [Fact]
        public async Task MoveFileAsync_ToItsOwnPath_ChangesNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            Clock.Advance(TimeSpan.FromMinutes(1));
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfFile same = await FileService.MoveFileAsync(shelf, EntryReference.ForPath("/a.txt"), "a.txt");

            Assert.Equal(file.UpdatedAt, same.UpdatedAt);
            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task MoveFileAsync_ToTheRoot_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");

            await Assert.ThrowsAsync<ArgumentException>(() => FileService.MoveFileAsync(shelf, EntryReference.ForPath("/a.txt"), "/"));
        }

        [Fact]
        public async Task SetDescriptionAsync_LogsTheFileWithItsDescriptionForReplicas()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await FileService.SetDescriptionAsync(shelf, EntryReference.ForPath("/a.txt"), "Signed copy");

            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.File, file.Id, ChangeAction.Updated), (logged.EntityType, logged.EntityId, logged.Action));
            Assert.Equal("Signed copy", ChangePayload.Deserialize<FileRecord>(logged.Payload!).Description);
        }

        [Fact]
        public async Task SetDescriptionAsync_NewText_MovesUpdatedAtToNow()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            Clock.Advance(TimeSpan.FromMinutes(1));

            ShelfFile described = await FileService.SetDescriptionAsync(shelf, EntryReference.ForPath("/a.txt"), "Signed copy");

            Assert.Equal(Clock.GetUtcNow(), described.UpdatedAt);
        }

        [Fact]
        public async Task SetDescriptionAsync_TheSameText_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile described = await FileService.SetDescriptionAsync(shelf, EntryReference.ForPath("/a.txt"), "note");
            Clock.Advance(TimeSpan.FromMinutes(1));
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfFile same = await FileService.SetDescriptionAsync(shelf, EntryReference.ForPath("/a.txt"), " note ");

            Assert.Equal(described.UpdatedAt, same.UpdatedAt);
            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task SetDescriptionAsync_OverTheLimit_ThrowsArgumentExceptionOnTheServerToo()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");

            await Assert.ThrowsAsync<ArgumentException>(
                () => FileService.SetDescriptionAsync(shelf, EntryReference.ForPath("/a.txt"), new string('a', FileDescription.MaxLength + 1)));
        }

        [Fact]
        public async Task StartAsync_DescriptionOverTheLimit_ThrowsBeforeReservingAnything()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => UploadService.StartAsync(
                shelf,
                new StartUploadRequest { Path = "/a.txt", SizeBytes = 1, Description = new string('a', FileDescription.MaxLength + 1) },
                Uploader));
        }

        [Fact]
        public async Task GetContentAsync_ByPath_PointsAtTheObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "contents");

            FileContent content = await FileService.GetContentAsync(shelf, EntryReference.ForPath("/a.txt"));

            Assert.Equal(file.Id, content.File.Id);
            Assert.Equal(ObjectStore.GetPath(file.Sha256), content.ObjectPath);
        }
    }
}
