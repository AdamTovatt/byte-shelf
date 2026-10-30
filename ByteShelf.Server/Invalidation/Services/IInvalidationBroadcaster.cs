using ByteShelf.Server.Invalidation.Models;

namespace ByteShelf.Server.Invalidation.Services
{
    /// <summary>
    /// Publish-side of the SSE invalidation pipeline. Services call <see cref="Publish"/>
    /// <b>after</b> their transaction commits — publishing inside the transaction can notify
    /// clients of state that then rolls back. Delivery is deliberately best-effort: events drop
    /// under backpressure, and clients recover via their reconnect-refetch path.
    /// </summary>
    public interface IInvalidationBroadcaster
    {
        void Publish(InvalidationEvent invalidationEvent);

        /// <summary>
        /// Opens a stream for <paramref name="callerKey"/>; null when that caller already holds
        /// <see cref="InMemoryInvalidationBroadcaster.MaxStreamsPerCaller"/> streams.
        /// </summary>
        IInvalidationSubscription? Subscribe(string callerKey);
    }
}
