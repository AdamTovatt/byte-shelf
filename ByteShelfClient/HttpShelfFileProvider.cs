using ByteShelfCommon;
using System.Net.Http.Json;
using System.Text.Json;

namespace ByteShelfClient
{
    /// <summary>
    /// HTTP-based implementation of <see cref="IShelfFileProvider"/> that communicates with a ByteShelf server.
    /// </summary>
    /// <remarks>
    /// This class provides a client-side implementation for interacting with a ByteShelf HTTP API server.
    /// It handles automatic chunking of large files, streaming content, and API key authentication.
    /// The <see cref="HttpClient"/> provided in the constructor should be configured with the appropriate
    /// base address and any required headers (except the API key, which is handled automatically).
    /// </remarks>
    public class HttpShelfFileProvider : IShelfFileProvider
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string? _apiKey;
        private int? _chunkSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpShelfFileProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for server communication.</param>
        /// <param name="apiKey">Optional API key for authentication. If provided, it will be added to all requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is null.</exception>
        /// <remarks>
        /// If an API key is provided, it will be automatically added to the <see cref="HttpClient.DefaultRequestHeaders"/>
        /// as "X-API-Key". The <see cref="HttpClient"/> should be configured with the appropriate base address.
        /// </remarks>
        public HttpShelfFileProvider(
            HttpClient httpClient,
            string? apiKey = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            // Set default API key header if provided
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }
        }

        /// <summary>
        /// Retrieves metadata for all stored files from the server.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/files" endpoint.
        /// Returns an empty collection if no files are found.
        /// </remarks>
        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(
            CancellationToken cancellationToken = default)
        {
            List<ShelfFileMetadata>? response = await _httpClient.GetFromJsonAsync<List<ShelfFileMetadata>>(
                "api/files",
                _jsonOptions,
                cancellationToken);

            return response ?? new List<ShelfFileMetadata>();
        }

        /// <summary>
        /// Reads a file by its ID, returning both metadata and content.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method first retrieves the file metadata from "/api/files/{fileId}/metadata",
        /// then creates a content provider that streams chunks from the server as needed.
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed.
        /// </remarks>
        public async Task<ShelfFile> ReadFileAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            // First get the metadata
            ShelfFileMetadata? metadata;
            try
            {
                metadata = await _httpClient.GetFromJsonAsync<ShelfFileMetadata>(
                    $"api/files/{fileId}/metadata",
                    _jsonOptions,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                throw new FileNotFoundException($"File with ID {fileId} not found", ex);
            }

            if (metadata == null)
                throw new FileNotFoundException($"File with ID {fileId} not found");

            // Create a content provider that will stream from HTTP chunks
            ChunkedHttpContentProvider contentProvider = new ChunkedHttpContentProvider(
                _httpClient,
                fileId,
                metadata.ChunkIds,
                cancellationToken);

            return new ShelfFile(metadata, contentProvider);
        }

        /// <summary>
        /// Writes a file to the server, automatically chunking it if necessary.
        /// </summary>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="originalFilename"/> or <paramref name="contentType"/> is empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when any HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server configuration cannot be retrieved.</exception>
        /// <remarks>
        /// This method:
        /// 1. Retrieves the chunk size configuration from the server
        /// 2. Splits the content into chunks if it exceeds the chunk size
        /// 3. Uploads each chunk to "/api/chunks/{chunkId}"
        /// 4. Creates and uploads the file metadata to "/api/files/metadata"
        /// The content stream will be read from its current position to the end.
        /// </remarks>
        public async Task<Guid> WriteFileAsync(
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            if (originalFilename == null) throw new ArgumentNullException(nameof(originalFilename));
            if (contentType == null) throw new ArgumentNullException(nameof(contentType));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(originalFilename)) throw new ArgumentException("Original filename cannot be empty", nameof(originalFilename));
            if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type cannot be empty", nameof(contentType));

            int chunkSize = await GetChunkSizeAsync(cancellationToken);
            Guid fileId = Guid.NewGuid();
            List<Guid> chunkIds = new List<Guid>();

            // Split the content into chunks
            byte[] buffer = new byte[chunkSize];
            int chunkIndex = 0;
            long totalBytesRead = 0;

            while (true)
            {
                int bytesRead = await content.ReadAsync(buffer, 0, chunkSize, cancellationToken);
                if (bytesRead == 0)
                    break;

                Guid chunkId = Guid.NewGuid();
                chunkIds.Add(chunkId);

                // Create a chunk stream
                using MemoryStream chunkStream = new MemoryStream(buffer, 0, bytesRead);

                // Upload the chunk
                HttpResponseMessage chunkResponse = await _httpClient.PutAsync(
                    $"api/chunks/{chunkId}",
                    new StreamContent(chunkStream),
                    cancellationToken);

                chunkResponse.EnsureSuccessStatusCode();

                totalBytesRead += bytesRead;
                chunkIndex++;
            }

            // Create and upload the metadata
            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                originalFilename,
                contentType,
                totalBytesRead,
                chunkIds);

            HttpResponseMessage metadataResponse = await _httpClient.PostAsJsonAsync(
                "api/files/metadata",
                metadata,
                _jsonOptions,
                cancellationToken);

            metadataResponse.EnsureSuccessStatusCode();

            return fileId;
        }

        /// <summary>
        /// Deletes a file and all its associated chunks from the server.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a DELETE request to "/api/files/{fileId}".
        /// The server is responsible for deleting both the file metadata and all associated chunks.
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// </remarks>
        public async Task DeleteFileAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response = await _httpClient.DeleteAsync(
                $"api/files/{fileId}",
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Gets storage usage information for the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Storage usage information including current usage and limits.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/storage" endpoint.
        /// Requires API key authentication.
        /// </remarks>
        public async Task<TenantStorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API key is required for tenant operations");

            TenantStorageInfo? response = await _httpClient.GetFromJsonAsync<TenantStorageInfo>(
                "api/tenant/storage",
                _jsonOptions,
                cancellationToken);

            if (response == null)
                throw new InvalidOperationException("Failed to retrieve storage information from server");

            return response;
        }

        /// <summary>
        /// Checks if the authenticated tenant can store a file of the specified size.
        /// </summary>
        /// <param name="fileSizeBytes">The size of the file to check in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Information about whether the file can be stored.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/storage/can-store" endpoint.
        /// This is useful for checking quota limits before attempting to upload large files.
        /// Requires API key authentication.
        /// </remarks>
        public async Task<QuotaCheckResult> CanStoreFileAsync(long fileSizeBytes, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API key is required for tenant operations");

            QuotaCheckResult? response = await _httpClient.GetFromJsonAsync<QuotaCheckResult>(
                $"api/tenant/storage/can-store?fileSizeBytes={fileSizeBytes}",
                _jsonOptions,
                cancellationToken);

            if (response == null)
                throw new InvalidOperationException("Failed to retrieve quota check result from server");

            return response;
        }

        /// <summary>
        /// Writes a file to the server with quota checking, automatically chunking it if necessary.
        /// </summary>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="checkQuotaFirst">Whether to check quota before uploading. Defaults to true.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="originalFilename"/> or <paramref name="contentType"/> is empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when any HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server configuration cannot be retrieved or quota is exceeded.</exception>
        /// <remarks>
        /// This method extends the base implementation with optional quota checking.
        /// If <paramref name="checkQuotaFirst"/> is true, it will check if the file can be stored
        /// before attempting the upload, helping to avoid failed uploads due to quota limits.
        /// Requires API key authentication.
        /// </remarks>
        public async Task<Guid> WriteFileWithQuotaCheckAsync(
            string originalFilename,
            string contentType,
            Stream content,
            bool checkQuotaFirst = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API key is required for quota checking");

            if (checkQuotaFirst)
            {
                // Get the content length if possible
                long contentLength = content.CanSeek ? content.Length : -1;

                if (contentLength > 0)
                {
                    // Check quota before uploading
                    QuotaCheckResult quotaCheck = await CanStoreFileAsync(contentLength, cancellationToken);
                    if (!quotaCheck.CanStore)
                    {
                        throw new InvalidOperationException(
                            $"Cannot store file: would exceed storage quota. " +
                            $"File size: {contentLength} bytes, " +
                            $"Available space: {quotaCheck.AvailableSpaceBytes} bytes");
                    }
                }
            }

            // Use the base implementation for the actual upload
            return await WriteFileAsync(originalFilename, contentType, content, cancellationToken);
        }

        /// <summary>
        /// Retrieves the chunk size configuration from the server.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk size in bytes.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server configuration cannot be retrieved.</exception>
        /// <remarks>
        /// This method caches the chunk size after the first successful retrieval.
        /// It makes a GET request to "/api/config/chunk-size".
        /// </remarks>
        private async Task<int> GetChunkSizeAsync(CancellationToken cancellationToken = default)
        {
            if (_chunkSize.HasValue)
                return _chunkSize.Value;

            ChunkConfiguration? config = await _httpClient.GetFromJsonAsync<ChunkConfiguration>(
                "api/config/chunk-size",
                _jsonOptions,
                cancellationToken);

            if (config == null)
                throw new InvalidOperationException("Failed to get chunk size configuration from server");

            _chunkSize = config.ChunkSizeBytes;
            return _chunkSize.Value;
        }

        private class ChunkedHttpContentProvider : IContentProvider, IDisposable
        {
            private readonly HttpClient _httpClient;
            private readonly Guid _fileId;
            private readonly List<Guid> _chunkIds;
            private readonly CancellationToken _cancellationToken;

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

            public Stream GetStream()
            {
                return new ChunkedStream(_httpClient, _chunkIds, _cancellationToken);
            }

            public void Dispose()
            {
                // Nothing to dispose here as the HttpClient is managed externally
            }
        }

        private class ChunkedStream : Stream
        {
            private readonly HttpClient _httpClient;
            private readonly List<Guid> _chunkIds;
            private readonly CancellationToken _cancellationToken;
            private int _currentChunkIndex;
            private Stream? _currentChunkStream;
            private long _position;

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

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count, _cancellationToken).GetAwaiter().GetResult();
            }

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

            private async Task LoadCurrentChunkAsync()
            {
                if (_currentChunkIndex >= _chunkIds.Count)
                    return;

                Guid chunkId = _chunkIds[_currentChunkIndex];
                HttpResponseMessage response = await _httpClient.GetAsync(
                    $"api/chunks/{chunkId}",
                    _cancellationToken);

                response.EnsureSuccessStatusCode();
                _currentChunkStream = await response.Content.ReadAsStreamAsync(_cancellationToken);
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _currentChunkStream?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private class ChunkConfiguration
        {
            public int ChunkSizeBytes { get; set; }
        }

        /// <summary>
        /// Information about a tenant's storage usage and limits.
        /// </summary>
        public class TenantStorageInfo
        {
            /// <summary>
            /// Gets or sets the tenant ID.
            /// </summary>
            public string TenantId { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the current storage usage in bytes.
            /// </summary>
            public long CurrentUsageBytes { get; set; }

            /// <summary>
            /// Gets or sets the storage limit in bytes.
            /// </summary>
            public long StorageLimitBytes { get; set; }

            /// <summary>
            /// Gets or sets the available storage space in bytes.
            /// </summary>
            public long AvailableSpaceBytes { get; set; }

            /// <summary>
            /// Gets or sets the usage percentage (0-100).
            /// </summary>
            public double UsagePercentage { get; set; }
        }

        /// <summary>
        /// Result of a quota check operation.
        /// </summary>
        public class QuotaCheckResult
        {
            /// <summary>
            /// Gets or sets the tenant ID.
            /// </summary>
            public string TenantId { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the size of the file being checked in bytes.
            /// </summary>
            public long FileSizeBytes { get; set; }

            /// <summary>
            /// Gets or sets whether the file can be stored.
            /// </summary>
            public bool CanStore { get; set; }

            /// <summary>
            /// Gets or sets the current storage usage in bytes.
            /// </summary>
            public long CurrentUsageBytes { get; set; }

            /// <summary>
            /// Gets or sets the storage limit in bytes.
            /// </summary>
            public long StorageLimitBytes { get; set; }

            /// <summary>
            /// Gets or sets the available storage space in bytes.
            /// </summary>
            public long AvailableSpaceBytes { get; set; }

            /// <summary>
            /// Gets or sets whether the file would exceed the quota.
            /// </summary>
            public bool WouldExceedQuota { get; set; }
        }
    }
}