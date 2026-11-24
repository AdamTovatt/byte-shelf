using System.Net.Http.Json;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
        };

        [Theory]
        [MemberData(nameof(ShelfContentsStates))]
        public void For_FileOrDirectory_MakesTheShelfListAndThatShelfsContentsStale(string entityType, object state)
        {
            IEnumerable<InvalidationEvent> events = For(Entry(entityType, ChangePayload.Serialize(state)));

            Assert.Equal(new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfContents, ShelfId) }, events);
        }

        [Fact]
        public void For_Grant_MakesTheShelfListAndThatShelfsAccessStale()
        {
            IEnumerable<InvalidationEvent> events = For(Entry(ChangeEntityType.ShelfGrant, ChangePayload.Serialize(ChangeStates.Grant(ShelfId))));

            Assert.Equal(new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfAccess, ShelfId) }, events);
        }

        [Fact]
        public void For_Key_MakesNothingStale()
        {
            Assert.Empty(For(Entry(ChangeEntityType.ShelfKey, ChangePayload.Serialize(ChangeStates.Key()))));
            Assert.Empty(For(Entry(ChangeEntityType.ShelfKey, payload: null)));
        }

        /// <summary>A shelf's key list shows each key's other shelves, so a key's grant on one shelf changes them all.</summary>
        [Fact]
        public void For_KeyGrant_MakesEveryShelfsAccessStale()
        {
            Assert.Equal(
                new[] { Event(InvalidationResources.ShelfAccess) },
                For(Entry(ChangeEntityType.ShelfKeyGrant, ChangePayload.Serialize(ChangeStates.KeyGrant(ShelfId)))));
            Assert.Equal(new[] { Event(InvalidationResources.ShelfAccess) }, For(Entry(ChangeEntityType.ShelfKeyGrant, payload: null)));
        }

        /// <summary>A purge leaves no state to name the shelf, so the same views go stale for every shelf.</summary>
        [Theory]
        [MemberData(nameof(ShelfScopedTypes))]
        public void For_Purged_MakesTheSameViewsStaleForEveryShelf(string entityType)
        {
            InvalidationEvent[] withState = For(Entry(entityType, ChangePayload.Serialize(ChangeStates.For(entityType, ShelfId)))).ToArray();

            IEnumerable<InvalidationEvent> purged = For(Entry(entityType, payload: null));

            Assert.Equal(withState.Select(invalidation => new InvalidationEvent(invalidation.Resource, null)), purged);
        }

        [Fact]
        public void For_Shelf_MakesEverythingAboutItStale()
        {
            IEnumerable<InvalidationEvent> events = For(Entry(ChangeEntityType.Shelf, payload: null, entityId: ShelfId));

            Assert.Equal(
                new[] { Event(InvalidationResources.Shelves), Event(InvalidationResources.ShelfContents, ShelfId), Event(InvalidationResources.ShelfAccess, ShelfId) },
                events);
        }

        [Fact]
        public void For_User_MakesTheUserListStale()
        {
            IEnumerable<InvalidationEvent> events = For(Entry(ChangeEntityType.User, "{}"));

            Assert.Equal(new[] { Event(InvalidationResources.Users) }, events);
        }

        [Fact]
        public void For_UsersLogin_MakesNothingStale()
        {
            IEnumerable<InvalidationEvent> events = For(Entry(ChangeEntityType.UserLogin, "{}"));

            Assert.Empty(events);
        }

        [Fact]
        public void For_DocumentAboutAFile_MakesItsShelfsContentsStale()
        {
            FilePluginMetadata document = FilePluginMetadata.Of("p", Guid.NewGuid(), 1, PluginDocument.Parse("{}"));
            ChangeLogEntry entry = Entry(ChangeEntityType.FilePluginMetadata, ChangePayload.Serialize(document));

            Assert.Equal(document.FileId, ChangeLogInvalidations.FileOf(entry));
            Assert.Equal(
                new[] { Event(InvalidationResources.ShelfContents, ShelfId) },
                ChangeLogInvalidations.For(entry, ShelfId));
        }

        [Fact]
        public void For_DocumentAboutAFileThatIsGone_MakesNothingStale()
        {
            FilePluginMetadata document = FilePluginMetadata.Of("p", Guid.NewGuid(), 1, PluginDocument.Parse("{}"));

            Assert.Empty(For(Entry(ChangeEntityType.FilePluginMetadata, ChangePayload.Serialize(document))));
        }

        [Fact]
        public void For_PurgedDocumentAboutAFile_MakesEveryShelfsContentsStale()
        {
            Assert.Equal(
                new[] { Event(InvalidationResources.ShelfContents) },
                For(Entry(ChangeEntityType.FilePluginMetadata, payload: null, action: ChangeAction.Purged)));
        }

        [Theory]
        [InlineData(ChangeAction.Added)]
        [InlineData(ChangeAction.Updated)]
        public void For_DocumentStateForgottenForANewer_MakesNothingStale(string action)
        {
            ChangeLogEntry forgotten = Entry(ChangeEntityType.FilePluginMetadata, payload: null, action: action);

            Assert.Null(ChangeLogInvalidations.FileOf(forgotten));
            Assert.Empty(For(forgotten));
        }

        [Theory]
        [InlineData(ChangeEntityType.ObjectMetadata)]
        [InlineData(ChangeEntityType.ContentPluginMetadata)]
        public void For_WhatWasReadFromContents_MakesNothingStale(string entityType)
        {
            Assert.Empty(For(Entry(entityType, "{}")));
            Assert.Empty(For(Entry(entityType, payload: null)));
        }

        [Fact]
        public async Task PublishAfterAsync_PluginDocumentsWritten_PublishesEachShelfsContents()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            List<ShelfFile> files = new List<ShelfFile>();
            foreach (string shelf in new[] { "docs", "photos" })
            {
                await host.CreateShelfAsync(shelf);
                files.Add(await host.PutAsAdminAsync(shelf, "/a.txt", "a"u8.ToArray(), "text/plain"));
            }

            long before = await host.Services.InTransactionAsync((provider, session) => provider.GetRequiredService<IChangeLogRepository>().GetLatestSequenceAsync(session));
            foreach (ShelfFile file in files)
            {
                await host.Services.SetFileDocumentAsync(file.Id, "{}");
            }

            // One of the test's own, beside the server's, whose poll publishes the upload whenever it comes to it.
            InMemoryInvalidationBroadcaster broadcaster = new InMemoryInvalidationBroadcaster();
            using IInvalidationSubscription subscription = broadcaster.Subscribe("test")!;
            using ChangeLogInvalidationPublisher publisher = new ChangeLogInvalidationPublisher(
                host.Services.GetRequiredService<IServiceScopeFactory>(), broadcaster, TimeProvider.System, NullLogger<ChangeLogInvalidationPublisher>.Instance);
            await publisher.PublishAfterAsync(before);

            List<InvalidationEvent> published = new List<InvalidationEvent>();
            while (subscription.Reader.TryRead(out InvalidationEvent? invalidation))
            {
                published.Add(invalidation);
            }

            Assert.Equal(files.Select(file => Event(InvalidationResources.ShelfContents, file.ShelfId)), published);
        }

        [Fact]
        public async Task Server_ShelfCreated_PublishesItToOpenStreams()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            using IInvalidationSubscription subscription = host.Services.GetRequiredService<IInvalidationBroadcaster>().Subscribe("test")!;

            HttpResponseMessage created = await host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" });
            created.EnsureSuccessStatusCode();
            InvalidationEvent expected = Event(InvalidationResources.ShelfContents, (await created.Content.ReadFromJsonAsync<ShelfInfo>())!.Id);

            // Other changes, such as the test host adding its users, may be published first; only this
            // shelf's own event names it. Reading times out (and fails the test) when it never comes.
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            InvalidationEvent received;
            do
            {
                received = await subscription.Reader.ReadAsync(timeout.Token);
            }
            while (received != expected);
        }

        private static IEnumerable<InvalidationEvent> For(ChangeLogEntry entry)
        {
            return ChangeLogInvalidations.For(entry, shelfOfFile: null);
        }

        private static ChangeLogEntry Entry(string entityType, string? payload, Guid? entityId = null, string? action = null)
        {
            return new ChangeLogEntry
            {
                Sequence = 1,
                EntityType = entityType,
                EntityId = entityId ?? Guid.NewGuid(),
                Action = action ?? (payload == null ? ChangeAction.Purged : ChangeAction.Updated),
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
