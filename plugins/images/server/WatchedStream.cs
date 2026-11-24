using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// A stream ImageMagick writes to, which takes no notice of one that throws, as one that is full does, and would leave
    /// what it had written as if it were all: this one remembers.
    /// </summary>
    internal sealed class WatchedStream : Stream
    {
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Whoever gave it disposes it.")]
        private readonly Stream _written;
        private ExceptionDispatchInfo? _failure;

        public WatchedStream(Stream written)
        {
            _written = written;
        }

        public override bool CanRead => false;

        public override bool CanSeek => _written.CanSeek;

        public override bool CanWrite => true;

        public override long Length => _written.Length;

        public override long Position
        {
            get => _written.Position;
            set => Seek(value, SeekOrigin.Begin);
        }

        /// <summary>Throws what the stream threw first, if it did.</summary>
        public void ThrowIfFailed()
        {
            _failure?.Throw();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Watch(() => _written.Write(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Watch(() => _written.Seek(offset, origin));
            return _written.Position;
        }

        public override void Flush()
        {
            Watch(_written.Flush);
        }

        public override void SetLength(long value)
        {
            Watch(() => _written.SetLength(value));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("It is written to alone.");
        }

        private void Watch(Action action)
        {
            ThrowIfFailed();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }
    }
}
