using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    public class ObjectCollectorTests : ServiceTestBase
    {
        private ObjectCollector Collector => Services.GetRequiredService<ObjectCollector>();

        [Fact]
        public async Task RemoveIfUnreferencedAsync_NoRowPointsAtIt_RemovesTheObject()
        {
            string sha256 = PlaceObject("orphan");

            bool removed = await Collector.RemoveIfUnreferencedAsync(sha256);

            Assert.True(removed);
            Assert.False(ObjectStore.Exists(sha256));
        }

        [Fact]
        public async Task RemoveIfUnreferencedAsync_UploadFinishesWhileItWaitsForTheLock_KeepsTheObject()
        {
            Shelf shelf = await CreateShelfAsync();
            string sha256 = PlaceObject("contended");

            Task<bool> removal;
            using (await ObjectStore.LockAsync(sha256))
            {
                // As a finishing upload does: hold the object's lock until the row pointing at it has
                // committed. The removal starts meanwhile and has to wait.
                removal = Collector.RemoveIfUnreferencedAsync(sha256);
                await Task.Delay(50);
                Assert.False(removal.IsCompleted);
                await InsertFileAsync(shelf, sha256);
            }

            Assert.False(await removal);
            Assert.True(ObjectStore.Exists(sha256));
        }

        private string PlaceObject(string text)
        {
            string sha256 = Sha256Of(text);
            string source = Path.Combine(TestRoot.Root.Path, Guid.NewGuid().ToString("N"));
            File.WriteAllText(source, text);
            ObjectStore.Place(source, sha256);
            return sha256;
        }

        private async Task InsertFileAsync(Shelf shelf, string sha256)
        {
            DateTimeOffset now = Clock.GetUtcNow();
            await using IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
            DirectoryRecord root = await Services.GetRequiredService<IDirectoryRepository>().GetRootAsync(shelf.Id, session);

            await Services.GetRequiredService<IFileRepository>().InsertAsync(
                new FileRecord
                {
                    Id = Guid.NewGuid(),
                    ShelfId = shelf.Id,
                    DirectoryId = root.Id,
                    Name = "file",
                    ContentType = ContentTypes.Default,
                    SizeBytes = 9,
                    Sha256 = sha256,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                session);

            await session.CommitAsync();
        }
    }
}
