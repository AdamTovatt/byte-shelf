namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// Contents as a plugin's process reads them: seekable, each read of a part asked of the server, which holds the
    /// file, so a plugin that needs little of a large one takes little. It answers one caller at a time.
    /// </summary>
    internal sealed class PipedContents : Stream
    {
        /// <summary>The least asked for at once, as parsers read a few bytes at a time.</summary>
        private const int BufferBytes = 64 * 1024;

        private readonly Lock _lock = new Lock();
        private readonly Func<ReadRequest, byte[]> _ask;
        private readonly long _length;
        private byte[] _buffer = [];
        private long _bufferOffset;
        private long _position;
        private bool _ended;

        /// <param name="length">How many bytes the contents are.</param>
        /// <param name="ask">Asks the server for a part of them, and answers it.</param>
        public PipedContents(long length, Func<ReadRequest, byte[]> ask)
        {
            _length = length;
            _ask = ask;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(Span<byte> buffer)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_ended, this);
                int count = (int)Math.Min(buffer.Length, Math.Max(0, _length - _position));
                if (count == 0)
                {
                    return 0;
                }

                if (_position < _bufferOffset || _position >= _bufferOffset + _buffer.Length)
                {
                    int asked = (int)Math.Min(Math.Clamp(count, BufferBytes, Frame.MaxPayloadBytes), _length - _position);
                    byte[] part = _ask(new ReadRequest(_position, asked));
                    if (part.Length != asked)
                    {
                        throw new EndOfStreamException("The contents end before their length.");
                    }

                    _buffer = part;
                    _bufferOffset = _position;
                }

                ReadOnlySpan<byte> rest = _buffer.AsSpan((int)(_position - _bufferOffset));
                count = Math.Min(count, rest.Length);
                rest[..count].CopyTo(buffer);
                _position += count;
                return count;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_lock)
            {
                long position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "There is no such origin."),
                };
                ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(offset));
                return _position = position;
            }
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Contents do not change.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Contents do not change.");
        }

        /// <summary>Waits for a read under way and refuses the next: none may be asked for once the run has been answered.</summary>
        protected override void Dispose(bool disposing)
        {
            lock (_lock)
            {
                _ended = true;
                _buffer = [];
            }

            base.Dispose(disposing);
        }
    }
}
