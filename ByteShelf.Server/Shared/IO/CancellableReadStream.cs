namespace ByteShelf.Server.Shared.IO
{
    /// <summary>
    /// Reads another stream, refusing once a token is cancelled, so code that reads it without looking at
    /// the token, such as a parsing library, still stops at its next read.
    /// </summary>
    internal sealed class CancellableReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _cancellationToken;

        public CancellableReadStream(Stream inner, CancellationToken cancellationToken)
        {
            _inner = inner;
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.Read(buffer);
        }

        public override int ReadByte()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.ReadByte();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.Seek(offset, origin);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
