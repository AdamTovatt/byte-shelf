using System.Threading.Channels;
using ByteShelf.Server.Invalidation.Models;

namespace ByteShelf.Server.Invalidation.Services
{
    public interface IInvalidationSubscription : IDisposable
    {
        ChannelReader<InvalidationEvent> Reader { get; }
    }
}
