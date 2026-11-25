namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>Some bytes, with zeroes after them, which are nowhere in memory.</summary>
    internal sealed class LongStream(byte[] start, long length) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - Position);
            Array.Clear(buffer, offset, read);
            if (Position < start.Length)
            {
                start.AsSpan((int)Position, Math.Min(read, start.Length - (int)Position)).CopyTo(buffer.AsSpan(offset));
            }

            Position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                _ => length + offset,
            };
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
