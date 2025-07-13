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
        private readonly string _apiKey;
        private int? _chunkSize;

        /// <summary>
        /// Creates and configures an HttpClient for use with ByteShelf.
        /// </summary>
        /// <param name="baseUrl">The base URL of the ByteShelf server (e.g., "https://localhost:7001" or "https://myserver.com").</param>
        /// <returns>A configured HttpClient with the base address set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is empty or whitespace.</exception>
        /// <remarks>
        /// This method creates a new HttpClient and sets its base address to the provided URL.
        /// The URL will be automatically normalized to end with a forward slash if it doesn't already.
        /// This is the recommended way to create an HttpClient for use with HttpShelfFileProvider.
        /// </remarks>
        public static HttpClient CreateHttpClient(string baseUrl)
        {
            if (baseUrl == null)
                throw new ArgumentNullException(nameof(baseUrl));

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL cannot be empty or whitespace", nameof(baseUrl));

            // Ensure the URL ends with a forward slash
            string normalizedUrl = baseUrl.TrimEnd('/') + "/";

            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(normalizedUrl);

            return httpClient;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpShelfFileProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for server communication.</param>
        /// <param name="apiKey">The API key for authentication. Cannot be null or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> or <paramref name="apiKey"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="apiKey"/> is empty or whitespace.</exception>
        /// <remarks>
        /// The API key will be automatically added to the <see cref="HttpClient.DefaultRequestHeaders"/>
        /// as "X-API-Key". The <see cref="HttpClient"/> should be configured with the appropriate base address.
        /// All operations require API key authentication to identify the tenant.
        /// </remarks>
        public HttpShelfFileProvider(
            HttpClient httpClient,
            string apiKey)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (apiKey == null)
                throw new ArgumentNullException(nameof(apiKey));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty or whitespace", nameof(apiKey));

            _apiKey = apiKey;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            // Set default API key header
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
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
                NormalizePath("api/files"),
                _jsonOptions,
                cancellationToken);

            return response ?? new List<ShelfFileMetadata>();
        }

        /// <summary>
        /// Retrieves metadata for all stored files from a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose files to retrieve.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files in the specified tenant.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated tenant does not have access to the specified tenant.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/files/{targetTenantId}" endpoint.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// Returns an empty collection if no files are found.
        /// </remarks>
        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesForTenantAsync(
            string targetTenantId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetTenantId))
                throw new ArgumentException("Target tenant ID cannot be null or empty", nameof(targetTenantId));

            try
            {
                List<ShelfFileMetadata>? response = await _httpClient.GetFromJsonAsync<List<ShelfFileMetadata>>(
                    NormalizePath($"api/files/{targetTenantId}"),
                    _jsonOptions,
                    cancellationToken);

                return response ?? new List<ShelfFileMetadata>();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'", ex);
            }
        }

        /// <summary>
        /// Reads a file by its ID, returning both metadata and content using the efficient single endpoint.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method uses the efficient single endpoint "/api/files/{fileId}/download" to retrieve the complete file.
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed.
        /// </remarks>
        public async Task<ShelfFile> ReadFileAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            return await ReadFileAsync(fileId, useChunked: false, cancellationToken);
        }

        /// <summary>
        /// Reads a file by its ID, returning both metadata and content with configurable retrieval method.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="useChunked">Whether to use the chunked endpoint approach. Defaults to false for better performance.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// When useChunked is false (default), this method uses the efficient single endpoint "/api/files/{fileId}/download".
        /// When useChunked is true, it uses the chunked approach with "/api/files/{fileId}/metadata" and "/api/chunks/{chunkId}".
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed.
        /// </remarks>
        public async Task<ShelfFile> ReadFileAsync(
            Guid fileId,
            bool useChunked,
            CancellationToken cancellationToken = default)
        {
            if (useChunked)
            {
                return await ReadFileViaChunkedEndpointAsync(fileId, cancellationToken);
            }
            else
            {
                return await ReadFileViaSingleEndpointAsync(fileId, cancellationToken);
            }
        }

        /// <summary>
        /// Reads a file by its ID from a specific tenant, returning both metadata and content.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to read.</param>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated tenant does not have access to the specified tenant.</exception>
        /// <remarks>
        /// This method uses the efficient single endpoint "/api/files/{targetTenantId}/{fileId}/download" to retrieve the complete file.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed.
        /// </remarks>
        public async Task<ShelfFile> ReadFileForTenantAsync(
            string targetTenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetTenantId))
                throw new ArgumentException("Target tenant ID cannot be null or empty", nameof(targetTenantId));

            try
            {
                // First get the metadata
                ShelfFileMetadata? metadata = await _httpClient.GetFromJsonAsync<ShelfFileMetadata>(
                    NormalizePath($"api/files/{targetTenantId}/{fileId}/metadata"),
                    _jsonOptions,
                    cancellationToken);

                if (metadata == null)
                    throw new FileNotFoundException($"File with ID {fileId} not found in tenant {targetTenantId}");

                // Get the complete file stream from the download endpoint
                HttpResponseMessage response = await _httpClient.GetAsync(
                    NormalizePath($"api/files/{targetTenantId}/{fileId}/download"),
                    cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileNotFoundException($"File with ID {fileId} not found in tenant {targetTenantId}");

                response.EnsureSuccessStatusCode();

                // Create a content provider that wraps the response stream
                SingleEndpointContentProvider contentProvider = new SingleEndpointContentProvider(
                    response.Content,
                    cancellationToken);

                return new ShelfFile(metadata, contentProvider);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'", ex);
            }
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
                    NormalizePath($"api/chunks/{chunkId}"),
                    new StreamContent(chunkStream),
                    cancellationToken);

                if (!chunkResponse.IsSuccessStatusCode)
                    throw new Exception($"Failed to write file: {await chunkResponse.Content.ReadAsStringAsync()}");

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
                NormalizePath("api/files/metadata"),
                metadata,
                _jsonOptions,
                cancellationToken);

            if (!metadataResponse.IsSuccessStatusCode)
                throw new Exception($"Failed to create file metadata: {await metadataResponse.Content.ReadAsStringAsync()}");

            return fileId;
        }

        /// <summary>
        /// Writes a file to a specific tenant on the server, automatically chunking it if necessary.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant for which to write the file.</param>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetTenantId"/>, <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="targetTenantId"/>, <paramref name="originalFilename"/>, or <paramref name="contentType"/> is empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when any HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server configuration cannot be retrieved.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated tenant does not have access to the specified tenant.</exception>
        /// <remarks>
        /// This method:
        /// 1. Retrieves the chunk size configuration from the server
        /// 2. Splits the content into chunks if it exceeds the chunk size
        /// 3. Uploads each chunk to "/api/chunks/{targetTenantId}/{chunkId}"
        /// 4. Creates and uploads the file metadata to "/api/files/{targetTenantId}/metadata"
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The content stream will be read from its current position to the end.
        /// </remarks>
        public async Task<Guid> WriteFileForTenantAsync(
            string targetTenantId,
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            if (targetTenantId == null) throw new ArgumentNullException(nameof(targetTenantId));
            if (originalFilename == null) throw new ArgumentNullException(nameof(originalFilename));
            if (contentType == null) throw new ArgumentNullException(nameof(contentType));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(targetTenantId)) throw new ArgumentException("Target tenant ID cannot be empty", nameof(targetTenantId));
            if (string.IsNullOrEmpty(originalFilename)) throw new ArgumentException("Original filename cannot be empty", nameof(originalFilename));
            if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type cannot be empty", nameof(contentType));

            try
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

                    // Upload the chunk to the specific tenant
                    HttpResponseMessage chunkResponse = await _httpClient.PutAsync(
                        NormalizePath($"api/chunks/{targetTenantId}/{chunkId}"),
                        new StreamContent(chunkStream),
                        cancellationToken);

                    if (!chunkResponse.IsSuccessStatusCode)
                        throw new Exception($"Failed to write file: {await chunkResponse.Content.ReadAsStringAsync()}");

                    totalBytesRead += bytesRead;
                    chunkIndex++;
                }

                // Create and upload the metadata to the specific tenant
                ShelfFileMetadata metadata = new ShelfFileMetadata(
                    fileId,
                    originalFilename,
                    contentType,
                    totalBytesRead,
                    chunkIds);

                HttpResponseMessage metadataResponse = await _httpClient.PostAsJsonAsync(
                    NormalizePath($"api/files/{targetTenantId}/metadata"),
                    metadata,
                    _jsonOptions,
                    cancellationToken);

                if (!metadataResponse.IsSuccessStatusCode)
                {
                    if (metadataResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'");

                    throw new Exception($"Failed to create file metadata: {await metadataResponse.Content.ReadAsStringAsync()}");
                }

                return fileId;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'", ex);
            }
        }

        /// <summary>
        /// Deletes a file and all its associated chunks from the server.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
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
                NormalizePath($"api/files/{fileId}"),
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new FileNotFoundException($"File with ID {fileId} not found");

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to delete file {fileId}: {await response.Content.ReadAsStringAsync()}");
        }

        /// <summary>
        /// Deletes a file and all its associated chunks from a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to delete.</param>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated tenant does not have access to the specified tenant.</exception>
        /// <remarks>
        /// This method makes a DELETE request to "/api/files/{targetTenantId}/{fileId}".
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The server is responsible for deleting both the file metadata and all associated chunks.
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// </remarks>
        public async Task DeleteFileForTenantAsync(
            string targetTenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetTenantId))
                throw new ArgumentException("Target tenant ID cannot be null or empty", nameof(targetTenantId));

            try
            {
                HttpResponseMessage response = await _httpClient.DeleteAsync(
                    NormalizePath($"api/files/{targetTenantId}/{fileId}"),
                    cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileNotFoundException($"File with ID {fileId} not found in tenant {targetTenantId}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'");

                    throw new Exception($"Failed to delete file {fileId} from tenant {targetTenantId}: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException($"Access denied to tenant '{targetTenantId}'", ex);
            }
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
            TenantStorageInfo? response = await _httpClient.GetFromJsonAsync<TenantStorageInfo>(
                NormalizePath("api/tenant/storage"),
                _jsonOptions,
                cancellationToken);

            if (response == null)
                throw new InvalidOperationException("Failed to retrieve storage information from server");

            return response;
        }

        /// <summary>
        /// Checks if the current tenant can store a file of the specified size.
        /// </summary>
        /// <param name="fileSizeBytes">The size of the file to check in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Information about whether the file can be stored.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no API key is provided.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/storage/can-store" endpoint.
        /// It allows clients to check if they can store a file of a specific size before attempting to upload it.
        /// Requires API key authentication.
        /// </remarks>
        public async Task<QuotaCheckResult> CanStoreFileAsync(long fileSizeBytes, CancellationToken cancellationToken = default)
        {
            QuotaCheckResult? response = await _httpClient.GetFromJsonAsync<QuotaCheckResult>(
                NormalizePath($"api/tenant/storage/can-store?fileSizeBytes={fileSizeBytes}"),
                _jsonOptions,
                cancellationToken);

            if (response == null)
                throw new HttpRequestException("Failed to get quota check result from server");

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
            if (originalFilename == null) throw new ArgumentNullException(nameof(originalFilename));
            if (contentType == null) throw new ArgumentNullException(nameof(contentType));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(originalFilename)) throw new ArgumentException("Original filename cannot be empty", nameof(originalFilename));
            if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type cannot be empty", nameof(contentType));

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
                NormalizePath("api/config/chunk-size"),
                _jsonOptions,
                cancellationToken);

            if (config == null)
                throw new InvalidOperationException("Failed to get chunk size configuration from server");

            _chunkSize = config.ChunkSizeBytes;
            return _chunkSize.Value;
        }

        /// <summary>
        /// Gets information about the current tenant including admin status and display name.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Tenant information including admin status, display name, and storage details.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no API key is provided.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/info" endpoint.
        /// It provides information about the tenant including their admin status, which is useful
        /// for frontend applications to determine what UI controls to show.
        /// Requires API key authentication.
        /// </remarks>
        public async Task<TenantInfoResponse> GetTenantInfoAsync(CancellationToken cancellationToken = default)
        {
            TenantInfoResponse? response = await _httpClient.GetFromJsonAsync<TenantInfoResponse>(
                NormalizePath("api/tenant/info"),
                _jsonOptions,
                cancellationToken);

            if (response == null)
                throw new HttpRequestException("Failed to get tenant information from server");

            return response;
        }

        /// <summary>
        /// Gets all subtenants of the current tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A dictionary of subtenants keyed by tenant ID.</returns>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/subtenants" endpoint.
        /// Returns an empty dictionary if the current tenant has no subtenants.
        /// </remarks>
        public async Task<Dictionary<string, TenantInfo>> GetSubTenantsAsync(CancellationToken cancellationToken = default)
        {
            Dictionary<string, TenantInfo>? response = await _httpClient.GetFromJsonAsync<Dictionary<string, TenantInfo>>(
                NormalizePath("api/tenant/subtenants"),
                _jsonOptions,
                cancellationToken);

            return response ?? new Dictionary<string, TenantInfo>();
        }

        /// <summary>
        /// Gets information about a specific subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The subtenant information.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a GET request to the "/api/tenant/subtenants/{subTenantId}" endpoint.
        /// </remarks>
        public async Task<TenantInfo> GetSubTenantAsync(string subTenantId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(subTenantId))
                throw new ArgumentException("Subtenant ID cannot be null or empty", nameof(subTenantId));

            try
            {
                TenantInfo? response = await _httpClient.GetFromJsonAsync<TenantInfo>(
                    NormalizePath($"api/tenant/subtenants/{subTenantId}"),
                    _jsonOptions,
                    cancellationToken);

                if (response == null)
                    throw new FileNotFoundException($"Subtenant with ID {subTenantId} not found");

                return response;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                throw new FileNotFoundException($"Subtenant with ID {subTenantId} not found", ex);
            }
        }

        /// <summary>
        /// Creates a new subtenant under the current tenant.
        /// </summary>
        /// <param name="displayName">The display name for the subtenant.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the created subtenant.</returns>
        /// <exception cref="ArgumentException">Thrown when display name is null or empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when maximum depth is reached.</exception>
        /// <remarks>
        /// This method makes a POST request to the "/api/tenant/subtenants" endpoint.
        /// The subtenant will have a unique ID and API key generated automatically.
        /// </remarks>
        public async Task<string> CreateSubTenantAsync(string displayName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name cannot be null or empty", nameof(displayName));

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                NormalizePath("api/tenant/subtenants"),
                request,
                _jsonOptions,
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Cannot create subtenant: {errorContent}");
            }

            response.EnsureSuccessStatusCode();

            try
            {
                // Parse the response using the proper typed response class
                CreateSubTenantResponse? result = await response.Content.ReadFromJsonAsync<CreateSubTenantResponse>(_jsonOptions, cancellationToken);
                if (result != null)
                {
                    return result.TenantId;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new HttpRequestException("Failed to parse create subtenant response", ex);
            }

            throw new HttpRequestException("Failed to get tenant ID from create subtenant response");
        }

        /// <summary>
        /// Creates a new subtenant under a specific subtenant.
        /// </summary>
        /// <param name="parentSubtenantId">The parent subtenant ID.</param>
        /// <param name="displayName">The display name for the subtenant.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the created subtenant.</returns>
        /// <exception cref="ArgumentException">Thrown when parent subtenant ID or display name is null or empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when maximum depth is reached.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the parent subtenant does not exist.</exception>
        /// <remarks>
        /// This method makes a POST request to the "/api/tenant/subtenants/{parentSubtenantId}/subtenants" endpoint.
        /// The subtenant will have a unique ID and API key generated automatically.
        /// The authenticated tenant must have access to the parent subtenant.
        /// </remarks>
        public async Task<string> CreateSubTenantUnderSubTenantAsync(string parentSubtenantId, string displayName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(parentSubtenantId))
                throw new ArgumentException("Parent subtenant ID cannot be null or empty", nameof(parentSubtenantId));

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Display name cannot be null or empty", nameof(displayName));

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                NormalizePath($"api/tenant/subtenants/{parentSubtenantId}/subtenants"),
                request,
                _jsonOptions,
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Cannot create subtenant: {errorContent}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Parent subtenant with ID {parentSubtenantId} not found");
            }

            response.EnsureSuccessStatusCode();

            try
            {
                // Parse the response using the proper typed response class
                CreateSubTenantResponse? result = await response.Content.ReadFromJsonAsync<CreateSubTenantResponse>(_jsonOptions, cancellationToken);
                if (result != null)
                {
                    return result.TenantId;
                }
            }
            catch (JsonException ex)
            {
                throw new HttpRequestException("Failed to parse create subtenant response", ex);
            }

            throw new HttpRequestException("Failed to get tenant ID from create subtenant response");
        }

        /// <summary>
        /// Updates the storage limit of a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="storageLimitBytes">The new storage limit in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="ArgumentException">Thrown when subtenant ID is null or empty, or storage limit is negative.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a PUT request to the "/api/tenant/subtenants/{subTenantId}/storage-limit" endpoint.
        /// The new limit cannot exceed the parent tenant's limit.
        /// </remarks>
        public async Task UpdateSubTenantStorageLimitAsync(string subTenantId, long storageLimitBytes, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(subTenantId))
                throw new ArgumentException("Subtenant ID cannot be null or empty", nameof(subTenantId));

            if (storageLimitBytes < 0)
                throw new ArgumentException("Storage limit cannot be negative", nameof(storageLimitBytes));

            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = storageLimitBytes
            };

            HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
                NormalizePath($"api/tenant/subtenants/{subTenantId}/storage-limit"),
                request,
                _jsonOptions,
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Subtenant with ID {subTenantId} not found");
            }

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Deletes a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="ArgumentException">Thrown when subtenant ID is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        /// <remarks>
        /// This method makes a DELETE request to the "/api/tenant/subtenants/{subTenantId}" endpoint.
        /// This operation cannot be undone.
        /// </remarks>
        public async Task DeleteSubTenantAsync(string subTenantId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(subTenantId))
                throw new ArgumentException("Subtenant ID cannot be null or empty", nameof(subTenantId));

            HttpResponseMessage response = await _httpClient.DeleteAsync(
                NormalizePath($"api/tenant/subtenants/{subTenantId}"),
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Subtenant with ID {subTenantId} not found");
            }

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Reads a file using the efficient single endpoint approach.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        private async Task<ShelfFile> ReadFileViaSingleEndpointAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            // First get the metadata
            ShelfFileMetadata? metadata;
            try
            {
                metadata = await _httpClient.GetFromJsonAsync<ShelfFileMetadata>(
                    NormalizePath($"api/files/{fileId}/metadata"),
                    _jsonOptions,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                throw new FileNotFoundException($"File with ID {fileId} not found", ex);
            }

            if (metadata == null)
                throw new FileNotFoundException($"File with ID {fileId} not found");

            // Get the complete file stream from the download endpoint
            HttpResponseMessage response = await _httpClient.GetAsync(
                NormalizePath($"api/files/{fileId}/download"),
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new FileNotFoundException($"File with ID {fileId} not found");

            response.EnsureSuccessStatusCode();

            // Create a content provider that wraps the response stream
            SingleEndpointContentProvider contentProvider = new SingleEndpointContentProvider(
                response.Content,
                cancellationToken);

            return new ShelfFile(metadata, contentProvider);
        }

        /// <summary>
        /// Reads a file using the chunked endpoint approach.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist on the server.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails or returns an error status code.</exception>
        private async Task<ShelfFile> ReadFileViaChunkedEndpointAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            // First get the metadata
            ShelfFileMetadata? metadata;
            try
            {
                metadata = await _httpClient.GetFromJsonAsync<ShelfFileMetadata>(
                    NormalizePath($"api/files/{fileId}/metadata"),
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
    }
}