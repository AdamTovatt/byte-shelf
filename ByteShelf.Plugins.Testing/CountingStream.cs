namespace ByteShelf.Plugins.Testing
{
    /// <summary>
    /// Contents that count how many bytes were read of them, as a test of how little a maker or reader reads. Every
    /// read comes to <see cref="Read"/>, which a stream's other reads go through when they are not overridden.
    /// </summary>
    public sealed class CountingStream(Stream contents) : Stream
    {
        private readonly Stream _contents = contents;
        private long _read;

        /// <summary>Of contents in memory.</summary>
        public CountingStream(byte[] contents)
            : this(new MemoryStream(contents, writable: false))
        {
        }

        public long BytesRead => Interlocked.Read(ref _read);

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _contents.Length;

        public override long Position
        {
            get => _contents.Position;
            set => _contents.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _contents.Read(buffer, offset, count);
            Interlocked.Add(ref _read, read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _contents.Seek(offset, origin);
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
                _contents.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
