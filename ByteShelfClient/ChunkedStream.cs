namespace ByteShelfClient
{
    /// <summary>
    /// A stream that reads from multiple HTTP chunks sequentially.
    /// </summary>
    internal class ChunkedStream : Stream
    {
        private readonly HttpClient _httpClient;
        private readonly List<Guid> _chunkIds;
        private readonly CancellationToken _cancellationToken;
        private int _currentChunkIndex;
        private Stream? _currentChunkStream;
        private long _position;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkedStream"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for chunk requests.</param>
        /// <param name="chunkIds">The list of chunk IDs to read from.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public ChunkedStream(
            HttpClient httpClient,
            List<Guid> chunkIds,
            CancellationToken cancellationToken)
        {
            _httpClient = httpClient;
            _chunkIds = chunkIds;
            _cancellationToken = cancellationToken;
            _currentChunkIndex = 0;
            _position = 0;
        }

        /// <summary>
        /// Normalizes a path for HTTP requests by ensuring it starts with a forward slash
        /// if the HttpClient's base address doesn't end with one.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // If the base address ends with a slash, we don't need to add one to the path
            if (_httpClient.BaseAddress != null && _httpClient.BaseAddress.ToString().EndsWith("/"))
                return path;

            // If the path already starts with a slash, return as-is
            if (path.StartsWith("/"))
                return path;

            // Add a leading slash
            return "/" + path;
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, _cancellationToken).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_currentChunkIndex >= _chunkIds.Count)
                return 0;

            if (_currentChunkStream == null)
            {
                await LoadCurrentChunkAsync();
            }

            int bytesRead = await _currentChunkStream!.ReadAsync(buffer, offset, count, cancellationToken);
            _position += bytesRead;

            if (bytesRead == 0 && _currentChunkIndex < _chunkIds.Count - 1)
            {
                _currentChunkIndex++;
                await LoadCurrentChunkAsync();
                bytesRead = await _currentChunkStream!.ReadAsync(buffer, offset, count, cancellationToken);
                _position += bytesRead;
            }

            return bytesRead;
        }

        /// <summary>
        /// Loads the current chunk from the server.
        /// </summary>
        private async Task LoadCurrentChunkAsync()
        {
            if (_currentChunkIndex >= _chunkIds.Count)
                return;

            Guid chunkId = _chunkIds[_currentChunkIndex];
            HttpResponseMessage response = await _httpClient.GetAsync(
                NormalizePath($"api/chunks/{chunkId}"),
                _cancellationToken);

            response.EnsureSuccessStatusCode();
            _currentChunkStream = await response.Content.ReadAsStreamAsync(_cancellationToken);
        }

        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _currentChunkStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}