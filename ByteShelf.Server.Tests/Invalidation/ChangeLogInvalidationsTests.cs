using System.Net.Http.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Invalidation
{
    /// <summary>What each change-log entry tells the web UI to refetch, and that the server publishes it.</summary>
    public class ChangeLogInvalidationsTests
    {
        private static readonly Guid ShelfId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        public static TheoryData<string, object> ShelfContentsStates => new TheoryData<string, object>
        {
            { ChangeEntityType.File, ChangeStates.File(ShelfId) },
            { ChangeEntityType.Directory, ChangeStates.Directory(ShelfId) },
        };

        public static TheoryData<string> ShelfScopedTypes => new TheoryData<string>
        {
            ChangeEntityType.File,
            ChangeEntityType.Directory,
            ChangeEntityType.ShelfGrant,
            ChangeEntityType.ShelfKey,
        };

        [Theory]
        [MemberData(nameof(ShelfContentsStates))]
        public void For_FileOrDirectory_MakesTheShelfListAndThatShelfsContentsStale(string entityType, object state)
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(entityType, ChangePayload.Serialize(state)));

            Assert.Equal(new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfContents, ShelfId) }, events);
        }

        [Fact]
        public void For_Grant_MakesTheShelfListAndThatShelfsAccessStale()
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(ChangeEntityType.ShelfGrant, ChangePayload.Serialize(ChangeStates.Grant(ShelfId))));

            Assert.Equal(new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfAccess, ShelfId) }, events);
        }

        [Fact]
        public void For_Key_MakesThatShelfsAccessStale()
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(ChangeEntityType.ShelfKey, ChangePayload.Serialize(ChangeStates.Key(ShelfId))));

            Assert.Equal(new[] { Event(InvalidationResources.ShelfAccess, ShelfId) }, events);
        }

        /// <summary>A purge leaves no state to name the shelf, so the same views go stale for every shelf.</summary>
        [Theory]
        [MemberData(nameof(ShelfScopedTypes))]
        public void For_Purged_MakesTheSameViewsStaleForEveryShelf(string entityType)
        {
            InvalidationEvent[] withState = ChangeLogInvalidations.For(Entry(entityType, ChangePayload.Serialize(ChangeStates.For(entityType, ShelfId)))).ToArray();

            IEnumerable<InvalidationEvent> purged = ChangeLogInvalidations.For(Entry(entityType, payload: null));

            Assert.Equal(withState.Select(invalidation => new InvalidationEvent(invalidation.Resource, null)), purged);
        }

        [Fact]
        public void For_Shelf_MakesEverythingAboutItStale()
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(ChangeEntityType.Shelf, payload: null, entityId: ShelfId));

            Assert.Equal(
                new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfContents, ShelfId), Event(InvalidationResources.ShelfAccess, ShelfId) },
                events);
        }

        [Fact]
        public void For_User_MakesTheUserListStale()
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(ChangeEntityType.User, "{}"));

            Assert.Equal(new[] { Event(InvalidationResources.Users) }, events);
        }

        [Fact]
        public void For_UsersLogin_MakesNothingStale()
        {
            IEnumerable<InvalidationEvent> events = ChangeLogInvalidations.For(Entry(ChangeEntityType.UserLogin, "{}"));

            Assert.Empty(events);
        }

        [Fact]
        public async Task Server_ShelfCreated_PublishesItToOpenStreams()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            using IInvalidationSubscription subscription = host.Services.GetRequiredService<IInvalidationBroadcaster>().Subscribe("test")!;

            (await host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();

            // Other changes, such as the test host adding its users, may be published first. Reading
            // times out (and fails the test) when the shelf's event never comes.
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            InvalidationEvent received;
            do
            {
                received = await subscription.Reader.ReadAsync(timeout.Token);
            }
            while (received.Resource != InvalidationResources.Shelves);
        }

        private static ChangeLogEntry Entry(string entityType, string? payload, Guid? entityId = null)
        {
            return new ChangeLogEntry
            {
                Sequence = 1,
                EntityType = entityType,
                EntityId = entityId ?? Guid.NewGuid(),
                Action = payload == null ? ChangeAction.Purged : ChangeAction.Updated,
                Payload = payload,
                CreatedAt = TestInstants.Now,
            };
        }

        private static InvalidationEvent Event(string resource, Guid? shelfId = null)
        {
            return new InvalidationEvent(resource, shelfId?.ToString("D"));
        }
    }
}
