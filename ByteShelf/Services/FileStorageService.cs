using ByteShelfCommon;
using System.Text.Json;

namespace ByteShelf.Services
{
    /// <summary>
    /// Implementation of file storage with tenant isolation and quota management.
    /// </summary>
    /// <remarks>
    /// This service provides tenant isolation by storing each tenant's files in separate
    /// subdirectories and enforces storage quotas. It uses the existing file storage
    /// infrastructure but scopes all operations to specific tenants.
    /// </remarks>
    public class FileStorageService : IFileStorageService
    {
        private readonly string _storagePath;
        private readonly IStorageService _storageService;
        private readonly ILogger<FileStorageService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileStorageService"/> class.
        /// </summary>
        /// <param name="storagePath">The base storage path for tenant data.</param>
        /// <param name="storageService">The storage service for quota management.</param>
        /// <param name="logger">The logger for recording service operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the parameters is null.</exception>
        /// <remarks>
        /// This constructor initializes the service with the specified storage path, storage service,
        /// and logger. The service will create tenant-specific directories under the storage path
        /// for organizing metadata and binary data by tenant.
        /// </remarks>
        public FileStorageService(
            string storagePath,
            IStorageService storageService,
            ILogger<FileStorageService> logger)
        {
            _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            _logger.LogInformation("FileStorageService initialized with storage path: {StoragePath}", _storagePath);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            List<ShelfFileMetadata> files = new List<ShelfFileMetadata>();
            string tenantMetadataPath = GetTenantMetadataPath(tenantId);

            if (!Directory.Exists(tenantMetadataPath))
            {
                return files;
            }

            string[] metadataFiles = Directory.GetFiles(tenantMetadataPath, "*.json");
            _logger.LogDebug("Found {Count} metadata files for tenant {TenantId}", metadataFiles.Length, tenantId);

            foreach (string metadataFile in metadataFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    string jsonContent = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                    ShelfFileMetadata? metadata = JsonSerializer.Deserialize<ShelfFileMetadata>(jsonContent, _jsonOptions);

                    if (metadata != null)
                    {
                        files.Add(metadata);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize metadata file for tenant {TenantId}: {File}", tenantId, metadataFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading metadata file for tenant {TenantId}: {File}", tenantId, metadataFile);
                }
            }

            return files;
        }

        /// <inheritdoc/>
        public async Task<ShelfFileMetadata?> GetFileMetadataAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            string tenantMetadataPath = GetTenantMetadataPath(tenantId);
            string metadataFile = Path.Combine(tenantMetadataPath, $"{fileId}.json");

            if (!File.Exists(metadataFile))
                return null;

            try
            {
                string jsonContent = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                return JsonSerializer.Deserialize<ShelfFileMetadata>(jsonContent, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading metadata file for tenant {TenantId}: {File}", tenantId, metadataFile);
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<Stream> GetChunkAsync(string tenantId, Guid chunkId, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            await Task.CompletedTask;

            string tenantBinPath = GetTenantBinPath(tenantId);
            string chunkFile = Path.Combine(tenantBinPath, $"{chunkId}.bin");

            if (!File.Exists(chunkFile))
                throw new FileNotFoundException($"Chunk with ID {chunkId} not found for tenant {tenantId}", chunkFile);

            return File.OpenRead(chunkFile);
        }

        /// <inheritdoc/>
        public async Task<Guid> SaveChunkAsync(string tenantId, Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            if (chunkData == null)
                throw new ArgumentNullException(nameof(chunkData));

            string tenantBinPath = GetTenantBinPath(tenantId);
            Directory.CreateDirectory(tenantBinPath); // Ensure tenant directory exists

            string chunkFile = Path.Combine(tenantBinPath, $"{chunkId}.bin");

            long chunkSize;
            using (FileStream fileStream = File.Create(chunkFile))
            {
                // Copy the data to the file
                await chunkData.CopyToAsync(fileStream, cancellationToken);
                chunkSize = fileStream.Length;
            }

            // Check quota after saving (since we can't know the size beforehand for non-seekable streams)
            if (!_storageService.CanStoreData(tenantId, chunkSize))
            {
                // Clean up the file we just created
                if (File.Exists(chunkFile))
                {
                    File.Delete(chunkFile);
                }
                throw new InvalidOperationException($"Tenant {tenantId} would exceed their storage quota by storing {chunkSize} bytes");
            }

            // Record the storage usage
            _storageService.RecordStorageUsed(tenantId, chunkSize);

            _logger.LogDebug("Saved chunk {ChunkId} for tenant {TenantId} ({SizeBytes} bytes)", chunkId, tenantId, chunkSize);
            return chunkId;
        }

        /// <inheritdoc/>
        public async Task SaveFileMetadataAsync(string tenantId, ShelfFileMetadata metadata, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            string tenantMetadataPath = GetTenantMetadataPath(tenantId);
            Directory.CreateDirectory(tenantMetadataPath); // Ensure tenant directory exists

            string metadataFile = Path.Combine(tenantMetadataPath, $"{metadata.Id}.json");
            string jsonContent = JsonSerializer.Serialize(metadata, _jsonOptions);

            await File.WriteAllTextAsync(metadataFile, jsonContent, cancellationToken);

            _logger.LogDebug("Saved metadata for file {FileId} for tenant {TenantId}", metadata.Id, tenantId);
        }

        /// <inheritdoc/>
        public async Task<bool?> DeleteFileAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default)
        {
            ValidateTenantId(tenantId);

            ShelfFileMetadata? metadata = await GetFileMetadataAsync(tenantId, fileId, cancellationToken);

            if (metadata == null)
                return null;

            long totalFreed = 0;
            string tenantBinPath = GetTenantBinPath(tenantId);

            // Delete all chunks and calculate freed space
            foreach (Guid chunkId in metadata.ChunkIds)
            {
                string chunkFile = Path.Combine(tenantBinPath, $"{chunkId}.bin");
                if (File.Exists(chunkFile))
                {
                    FileInfo fileInfo = new FileInfo(chunkFile);
                    totalFreed += fileInfo.Length;
                    File.Delete(chunkFile);
                    _logger.LogDebug("Deleted chunk {ChunkId} for tenant {TenantId}", chunkId, tenantId);
                }
            }

            // Delete metadata file
            string tenantMetadataPath = GetTenantMetadataPath(tenantId);
            string metadataFile = Path.Combine(tenantMetadataPath, $"{fileId}.json");
            if (File.Exists(metadataFile))
            {
                File.Delete(metadataFile);
                _logger.LogDebug("Deleted metadata for file {FileId} for tenant {TenantId}", fileId, tenantId);
            }

            // Record the freed storage
            if (totalFreed > 0)
            {
                _storageService.RecordStorageFreed(tenantId, totalFreed);
                _logger.LogInformation("Freed {FreedBytes} bytes for tenant {TenantId} by deleting file {FileId}", totalFreed, tenantId, fileId);
            }

            return true;
        }

        /// <inheritdoc/>
        public bool CanStoreFile(string tenantId, long fileSizeBytes)
        {
            ValidateTenantId(tenantId);
            return _storageService.CanStoreData(tenantId, fileSizeBytes);
        }

        /// <summary>
        /// Gets the metadata directory path for a specific tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The path to the tenant's metadata directory.</returns>
        private string GetTenantMetadataPath(string tenantId)
        {
            return Path.Combine(_storagePath, tenantId, "metadata");
        }

        /// <summary>
        /// Gets the binary storage directory path for a specific tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The path to the tenant's binary storage directory.</returns>
        private string GetTenantBinPath(string tenantId)
        {
            return Path.Combine(_storagePath, tenantId, "bin");
        }

        /// <summary>
        /// Validates that a tenant ID is not null or empty.
        /// </summary>
        /// <param name="tenantId">The tenant ID to validate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is empty or whitespace.</exception>
        private static void ValidateTenantId(string tenantId)
        {
            if (tenantId == null)
                throw new ArgumentNullException(nameof(tenantId));

            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("Tenant ID cannot be empty or whitespace", nameof(tenantId));
        }
    }
}