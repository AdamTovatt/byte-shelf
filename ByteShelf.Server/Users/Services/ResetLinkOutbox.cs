using System.Threading.Channels;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Reset links waiting to be sent by <see cref="ResetLinkSender"/>. A reset request hands its link
    /// here and answers at once, so how long the send takes cannot tell a caller whether the email has
    /// an account. Bounded: when it is full a link is dropped and the user asks again.
    /// </summary>
    public sealed class ResetLinkOutbox
    {
        private const int Capacity = 100;

        private readonly Channel<ResetLink> _channel = Channel.CreateBounded<ResetLink>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

        /// <summary>Queues a link; false when the outbox is full and the link was dropped.</summary>
        public bool TryEnqueue(ResetLink link)
        {
            return _channel.Writer.TryWrite(link);
        }

        public IAsyncEnumerable<ResetLink> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
