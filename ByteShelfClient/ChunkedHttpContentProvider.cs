using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// Content provider that streams chunks from HTTP endpoints.
    /// </summary>
    internal class ChunkedHttpContentProvider : IContentProvider, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Guid _fileId;
        private readonly List<Guid> _chunkIds;
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkedHttpContentProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for chunk requests.</param>
        /// <param name="fileId">The file ID.</param>
        /// <param name="chunkIds">The list of chunk IDs for this file.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public ChunkedHttpContentProvider(
            HttpClient httpClient,
            Guid fileId,
            List<Guid> chunkIds,
            CancellationToken cancellationToken)
        {
            _httpClient = httpClient;
            _fileId = fileId;
            _chunkIds = chunkIds;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets a stream that reads from the HTTP chunks.
        /// </summary>
        /// <returns>A stream that reads from the HTTP chunks.</returns>
        public Stream GetStream()
        {
            return new ChunkedStream(_httpClient, _chunkIds, _cancellationToken);
        }

        /// <summary>
        /// Disposes the content provider.
        /// </summary>
        public void Dispose()
        {
            // Nothing to dispose here as the HttpClient is managed externally
        }
    }
}