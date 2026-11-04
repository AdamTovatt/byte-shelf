using System.Security.Cryptography;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// A file's contents as the server sends them, disposing the response with it so a download
    /// releases its connection. When the response names the contents' SHA-256 (the server's ETag), the
    /// bytes are hashed as they are read, and reading to the end throws if they do not match.
    /// </summary>
    internal sealed class ResponseStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _body;
        private readonly string? _expectedSha256;
        private readonly IncrementalHash? _hash;
        private bool _verified;

        /// <param name="response">The response the body belongs to.</param>
        /// <param name="body">Its content.</param>
        /// <param name="expectedSha256">The whole contents' SHA-256 in hex, or null to read without checking.</param>
        public ResponseStream(HttpResponseMessage response, Stream body, string? expectedSha256)
        {
            _response = response;
            _body = body;
            _expectedSha256 = expectedSha256;
            _hash = expectedSha256 == null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
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
            int read = _body.Read(buffer, offset, count);
            Account(buffer.AsSpan(offset, read), count);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Account(buffer.Span[..read], buffer.Length);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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
                _hash?.Dispose();
                _body.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Hashes what was read, and at the end of the contents compares the hash with the expected one.</summary>
        /// <exception cref="IOException">The contents do not match their SHA-256.</exception>
        private void Account(ReadOnlySpan<byte> read, int requested)
        {
            if (_hash == null || _verified)
            {
                return;
            }

            _hash.AppendData(read);
            if (read.Length > 0 || requested == 0)
            {
                return;
            }

            _verified = true;
            string actual = Convert.ToHexString(_hash.GetHashAndReset());
            if (!string.Equals(actual, _expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"The download does not match the file's SHA-256: expected {_expectedSha256}, got {actual.ToLowerInvariant()}.");
            }
        }
    }
}
