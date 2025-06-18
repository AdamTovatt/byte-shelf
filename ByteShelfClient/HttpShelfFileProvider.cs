using System.Net.Http.Json;
using System.Text.Json;
using ByteShelfCommon;

namespace ByteShelfClient
{
    public class HttpShelfFileProvider : IShelfFileProvider
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string? _apiKey;
        private int? _chunkSize;

        public HttpShelfFileProvider(
            HttpClient httpClient,
            string? apiKey = null)
        {
            _httpClient = httpClient;
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

        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(
            CancellationToken cancellationToken = default)
        {
            List<ShelfFileMetadata>? response = await _httpClient.GetFromJsonAsync<List<ShelfFileMetadata>>(
                "api/files",
                _jsonOptions,
                cancellationToken);

            return response ?? new List<ShelfFileMetadata>();
        }

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

        public async Task<Guid> WriteFileAsync(
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
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

        public async Task DeleteFileAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response = await _httpClient.DeleteAsync(
                $"api/files/{fileId}",
                cancellationToken);

            response.EnsureSuccessStatusCode();
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
    }
}