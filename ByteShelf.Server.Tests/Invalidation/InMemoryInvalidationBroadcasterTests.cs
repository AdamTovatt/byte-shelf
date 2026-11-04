using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;

namespace ByteShelf.Server.Tests.Invalidation
{
    public class InMemoryInvalidationBroadcasterTests
    {
        [Fact]
        public async Task Publish_WithActiveSubscription_DeliversEvent()
        {
            InMemoryInvalidationBroadcaster broadcaster = new InMemoryInvalidationBroadcaster();
            using IInvalidationSubscription subscription = broadcaster.Subscribe("caller")!;

            broadcaster.Publish(new InvalidationEvent(InvalidationResources.Users));

            Assert.True(await subscription.Reader.WaitToReadAsync());
            Assert.True(subscription.Reader.TryRead(out InvalidationEvent? received));
            Assert.Equal(InvalidationResources.Users, received.Resource);
        }

        [Fact]
        public void Publish_AfterSubscriptionDisposed_DoesNotThrow()
        {
            InMemoryInvalidationBroadcaster broadcaster = new InMemoryInvalidationBroadcaster();
            broadcaster.Subscribe("caller")!.Dispose();

            broadcaster.Publish(new InvalidationEvent(InvalidationResources.Users));
        }

        [Fact]
        public void Publish_MultipleSubscribers_EachReceivesTheEvent()
        {
            InMemoryInvalidationBroadcaster broadcaster = new InMemoryInvalidationBroadcaster();
            using IInvalidationSubscription first = broadcaster.Subscribe("one")!;
            using IInvalidationSubscription second = broadcaster.Subscribe("two")!;

            broadcaster.Publish(new InvalidationEvent(InvalidationResources.Users, "metadata"));

            Assert.True(first.Reader.TryRead(out InvalidationEvent? firstEvent));
            Assert.True(second.Reader.TryRead(out InvalidationEvent? secondEvent));
            Assert.Equal("metadata", firstEvent.Metadata);
            Assert.Equal("metadata", secondEvent.Metadata);
        }

        [Fact]
        public void Subscribe_CallerAtTheLimit_RefusesUntilAStreamCloses()
        {
            InMemoryInvalidationBroadcaster broadcaster = new InMemoryInvalidationBroadcaster();
            List<IInvalidationSubscription> open = Enumerable.Range(0, InMemoryInvalidationBroadcaster.MaxStreamsPerCaller)
                .Select(_ => broadcaster.Subscribe("caller")!)
                .ToList();

            Assert.Null(broadcaster.Subscribe("caller"));
            using IInvalidationSubscription other = broadcaster.Subscribe("someone else")!;
            Assert.NotNull(other);

            open[0].Dispose();
            using IInvalidationSubscription again = broadcaster.Subscribe("caller")!;
            Assert.NotNull(again);
        }
    }
}
