using System.Diagnostics.CodeAnalysis;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// A seekable stream from the position it stood at to its length then, as a stream of its own. A
    /// handler that rewinds a request body before sending it (the ASP.NET Core test server does) then
    /// starts where the caller's stream stood, and what is sent matches the length declared up front.
    /// Disposing it leaves the caller's stream open.
    /// </summary>
    internal sealed class StreamTail : Stream
    {
        [SuppressMessage("Usage", "CA2213", Justification = "The caller's stream, which the caller disposes.")]
        private readonly Stream _inner;
        private readonly long _start;

        public StreamTail(Stream inner)
        {
            _inner = inner;
            _start = inner.Position;
            Length = inner.Length - _start;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length { get; }

        public override long Position
        {
            get => _inner.Position - _start;
            set => Seek(value, SeekOrigin.Begin);
        }

        private int Remaining(int requested)
        {
            return (int)Math.Clamp(Length - Position, 0, requested);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, Remaining(count));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, Remaining(count), cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer[..Remaining(buffer.Length)], cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
            _inner.Position = _start + target;
            return target;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
