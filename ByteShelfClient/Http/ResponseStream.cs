namespace ByteShelfClient.Http
{
    /// <summary>A response's body that disposes the response with it, so a download releases its connection.</summary>
    internal sealed class ResponseStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _body;

        public ResponseStream(HttpResponseMessage response, Stream body)
        {
            _response = response;
            _body = body;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _response.Content.Headers.ContentLength ?? throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _body.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _body.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _body.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
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
                _body.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
