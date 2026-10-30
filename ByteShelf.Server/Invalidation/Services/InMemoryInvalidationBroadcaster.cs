using System.Threading.Channels;
using ByteShelf.Server.Invalidation.Models;

namespace ByteShelf.Server.Invalidation.Services
{
    public class InMemoryInvalidationBroadcaster : IInvalidationBroadcaster
    {
        /// <summary>How many streams one caller may hold open at once: several tabs and devices, not an unbounded number.</summary>
        public const int MaxStreamsPerCaller = 10;

        private const int ChannelCapacity = 100;

        private readonly Dictionary<Guid, Subscriber> _subscribers = new Dictionary<Guid, Subscriber>();
        private readonly Dictionary<string, int> _streamsPerCaller = new Dictionary<string, int>(StringComparer.Ordinal);

        public void Publish(InvalidationEvent invalidationEvent)
        {
            List<Subscriber> subscribers;
            lock (_subscribers)
            {
                subscribers = _subscribers.Values.ToList();
            }

            foreach (Subscriber subscriber in subscribers)
            {
                subscriber.Channel.Writer.TryWrite(invalidationEvent);
            }
        }

        public IInvalidationSubscription? Subscribe(string callerKey)
        {
            Channel<InvalidationEvent> channel = Channel.CreateBounded<InvalidationEvent>(
                new BoundedChannelOptions(ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });

            Guid id = Guid.NewGuid();

            lock (_subscribers)
            {
                int streams = _streamsPerCaller.GetValueOrDefault(callerKey);
                if (streams >= MaxStreamsPerCaller)
                {
                    return null;
                }

                _streamsPerCaller[callerKey] = streams + 1;
                _subscribers[id] = new Subscriber(callerKey, channel);
            }

            return new Subscription(this, id, channel);
        }

        private void Unsubscribe(Guid id)
        {
            Subscriber? subscriber;
            lock (_subscribers)
            {
                if (!_subscribers.Remove(id, out subscriber))
                {
                    return;
                }

                int remaining = _streamsPerCaller[subscriber.CallerKey] - 1;
                if (remaining == 0)
                {
                    _streamsPerCaller.Remove(subscriber.CallerKey);
                }
                else
                {
                    _streamsPerCaller[subscriber.CallerKey] = remaining;
                }
            }

            subscriber.Channel.Writer.TryComplete();
        }

        private sealed record Subscriber(string CallerKey, Channel<InvalidationEvent> Channel);

        private sealed class Subscription : IInvalidationSubscription
        {
            private readonly InMemoryInvalidationBroadcaster _broadcaster;
            private readonly Guid _id;
            private readonly Channel<InvalidationEvent> _channel;
            private bool _disposed;

            public Subscription(InMemoryInvalidationBroadcaster broadcaster, Guid id, Channel<InvalidationEvent> channel)
            {
                _broadcaster = broadcaster;
                _id = id;
                _channel = channel;
            }

            public ChannelReader<InvalidationEvent> Reader => _channel.Reader;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _broadcaster.Unsubscribe(_id);
            }
        }
    }
}
