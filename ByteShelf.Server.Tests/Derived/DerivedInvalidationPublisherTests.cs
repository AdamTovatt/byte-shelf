using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Which open listings are told that derived files came or went, and when.</summary>
    public sealed class DerivedInvalidationPublisherTests : DerivedTestBase
    {
        // Written out rather than taken from the code, so a change of it is noticed.
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly RecordingBroadcaster _broadcaster = new RecordingBroadcaster();
        private readonly RecordingLogger<DerivedInvalidationPublisher> _logger = new RecordingLogger<DerivedInvalidationPublisher>();

        [Fact]
        public async Task Made_TellsTheShelvesWithALivePlainFileOfIt()
        {
            Shelf one = await CreateShelfAsync("one");
            Shelf two = await CreateShelfAsync("two");
            Shelf deleted = await CreateShelfAsync("deleted");
            Shelf secret = await CreateShelfAsync("secret");
            Shelf other = await CreateShelfAsync("other");
            ShelfFile file = await StoreAsync(one, "/a.txt", AgeBytes, "text/plain");
            await StoreAsync(two, "/a.txt", AgeBytes, "text/plain");
            await StoreAsync(deleted, "/a.txt", AgeBytes, "text/plain");
            await FileService.DeleteFileAsync(deleted, EntryReference.ForPath("/a.txt"));
            await StoreAsync(secret, "/a.txt", AgeBytes, "text/plain", encrypted: true);
            await StoreTextAsync(other, "/a.txt", "never made");
            using DerivedInvalidationPublisher publisher = await StartAsync();

            await GetAsync(one, file);

            await PublishedAsync(one, two);
            await publisher.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Made_TellsAtOnceAndThenAtMostEveryFiveSeconds()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] files = [await StoreTextAsync(shelf, "/a.txt", "a"), await StoreTextAsync(shelf, "/b.txt", "b"), await StoreTextAsync(shelf, "/c.txt", "c")];
            using DerivedInvalidationPublisher publisher = await StartAsync();

            await GetAsync(shelf, files[0]);
            await PublishedAsync(shelf);

            await GetAsync(shelf, files[1]);
            await GetAsync(shelf, files[2]);
            await Clock.WaitForTimerAsync(Interval);
            Clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
            Assert.Single(_broadcaster.Shelves);
            Clock.Advance(TimeSpan.FromMilliseconds(1));

            await PublishedAsync(shelf, shelf);
            await publisher.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Removed_TellsToo()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await GetAsync(shelf, file);
            Services.GetRequiredService<DerivedChanges>().Take();
            using DerivedInvalidationPublisher publisher = await StartAsync();

            using (await Locks.LockAsync(file.Sha256))
            {
                Store.Delete(DerivedKind.Thumbnail, file.Sha256);
            }

            await PublishedAsync(shelf);
            await publisher.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Publishing_ThatFails_IsLoggedAndGoesOn()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile second = await StoreTextAsync(shelf, "/b.txt", "b");
            using DerivedInvalidationPublisher publisher = await StartAsync();
            _broadcaster.Failing = true;
            await GetAsync(shelf, first);
            await Clock.WaitForTimerAsync(Interval);
            _broadcaster.Failing = false;
            Assert.Equal(["It was told to fail."], _logger.Exceptions.Select(exception => exception.Message));

            await GetAsync(shelf, second);
            Clock.Advance(Interval);

            await PublishedAsync(shelf);
            await publisher.StopAsync(CancellationToken.None);
        }

        [Theory]
        [InlineData("throw", false)]
        [InlineData("a", true)]
        public async Task OnlyWhatWasMade_IsAChange(string contents, bool expected)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);

            await MakeAsync(file);

            Assert.Equal(expected ? [file.Sha256] : [], Services.GetRequiredService<DerivedChanges>().Take());
        }

        private async Task<DerivedInvalidationPublisher> StartAsync()
        {
            DerivedInvalidationPublisher publisher = new DerivedInvalidationPublisher(
                Services.GetRequiredService<DerivedChanges>(), Services.GetRequiredService<IServiceScopeFactory>(), _broadcaster, Clock, _logger);
            await publisher.StartAsync(CancellationToken.None);
            return publisher;
        }

        /// <summary>Waits until as much was published as these shelves, which it then was.</summary>
        private async Task PublishedAsync(params Shelf[] shelves)
        {
            await TestDerivedPlugins.UntilAsync(() => _broadcaster.Shelves.Count >= shelves.Length).WaitAsync(RealTimeLimit);
            Assert.Equal(shelves.Select(shelf => shelf.Id).Order(), _broadcaster.Shelves.Order());
        }

        private sealed class RecordingBroadcaster : IInvalidationBroadcaster
        {
            private readonly List<Guid> _shelves = new List<Guid>();

            public bool Failing { get; set; }

            public IReadOnlyList<Guid> Shelves
            {
                get
                {
                    lock (_shelves)
                    {
                        return _shelves.ToList();
                    }
                }
            }

            public void Publish(InvalidationEvent invalidationEvent)
            {
                if (Failing)
                {
                    throw new InvalidOperationException("It was told to fail.");
                }

                Assert.Equal(InvalidationResources.ShelfContents, invalidationEvent.Resource);
                lock (_shelves)
                {
                    _shelves.Add(Guid.Parse(invalidationEvent.Metadata!));
                }
            }

            public IInvalidationSubscription? Subscribe(string callerKey)
            {
                throw new NotSupportedException();
            }
        }
    }
}
