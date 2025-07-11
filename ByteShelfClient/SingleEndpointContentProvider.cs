using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// Content provider that wraps a single HTTP response stream.
    /// </summary>
    internal class SingleEndpointContentProvider : IContentProvider, IDisposable
    {
        private readonly HttpContent _httpContent;
        private readonly CancellationToken _cancellationToken;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleEndpointContentProvider"/> class.
        /// </summary>
        /// <param name="httpContent">The HTTP content to wrap.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public SingleEndpointContentProvider(
            HttpContent httpContent,
            CancellationToken cancellationToken)
        {
            _httpContent = httpContent ?? throw new ArgumentNullException(nameof(httpContent));
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets a stream that reads from the HTTP content.
        /// </summary>
        /// <returns>A stream that reads from the HTTP content.</returns>
        public Stream GetStream()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SingleEndpointContentProvider));

            return _httpContent.ReadAsStreamAsync(_cancellationToken).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Disposes the content provider.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _httpContent?.Dispose();
                _disposed = true;
            }
        }
    }
} 