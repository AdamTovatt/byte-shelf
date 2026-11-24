namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>A stream that throws once it would be over so many bytes, as the server's for a derived file does.</summary>
    internal sealed class LimitedStream(long maxBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Reach(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reach(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reach(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reach(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void Reach(int count)
        {
            if (Position + count > maxBytes)
            {
                throw new IOException($"A derived file may take {maxBytes} bytes.");
            }
        }
    }
}
