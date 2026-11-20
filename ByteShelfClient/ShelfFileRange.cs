using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// Part of a file's contents, as <see cref="IShelfClient.OpenReadRangeAsync"/> opens it: the bytes from
    /// <see cref="Offset"/>, <see cref="Length"/> of them, out of the <see cref="TotalLength"/> the file holds.
    /// Disposing it disposes <see cref="Content"/>.
    /// </summary>
    public sealed class ShelfFileRange : IDisposable, IAsyncDisposable
    {
        /// <param name="content">The range's bytes, to be read from their start.</param>
        /// <param name="offset">Where in the file the range starts.</param>
        /// <param name="length">How many bytes the range holds.</param>
        /// <param name="totalLength">How many bytes the whole file holds.</param>
        /// <param name="sha256">The SHA-256 of the contents the range was read from.</param>
        /// <exception cref="ArgumentOutOfRangeException">The range does not lie within the file.</exception>
        /// <exception cref="ArgumentException"><paramref name="sha256"/> is not 64 hex digits.</exception>
        public ShelfFileRange(Stream content, long offset, long length, long totalLength, string sha256)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfLessThan(totalLength, offset + length);

            Content = content;
            Offset = offset;
            Length = length;
            TotalLength = totalLength;
            Sha256 = ContentHash.Normalize(sha256);
        }

        /// <summary>The range's bytes. Only a range that is the whole file is checked against <see cref="Sha256"/>.</summary>
        public Stream Content { get; }

        /// <summary>Where in the file the range starts.</summary>
        public long Offset { get; }

        /// <summary>How many bytes the range holds.</summary>
        public long Length { get; }

        /// <summary>How many bytes the whole file holds.</summary>
        public long TotalLength { get; }

        /// <summary>The SHA-256 of the contents the range was read from, lowercase.</summary>
        public string Sha256 { get; }

        /// <summary>Disposes <see cref="Content"/>.</summary>
        public void Dispose()
        {
            Content.Dispose();
        }

        /// <inheritdoc cref="Dispose"/>
        public ValueTask DisposeAsync()
        {
            return Content.DisposeAsync();
        }
    }
}
