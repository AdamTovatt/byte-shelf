using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ByteShelfCommon;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Services
{
    /// <summary>
    /// File system-based implementation of <see cref="IFileStorageService"/>.
    /// </summary>
    /// <remarks>
    /// This service stores files on the local file system using a structured directory layout:
    /// - Metadata files are stored as JSON in a "metadata" subdirectory
    /// - Chunk files are stored as binary data in a "bin" subdirectory
    /// The service automatically creates the necessary directories if they don't exist.
    /// </remarks>
    public class FileStorageService : IFileStorageService
    {
        private readonly string _storagePath;
        private readonly string _metadataPath;
        private readonly string _binPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<FileStorageService>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileStorageService"/> class.
        /// </summary>
        /// <param name="storagePath">The root directory path for file storage.</param>
        /// <param name="logger">Optional logger for diagnostic information.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="storagePath"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="storagePath"/> is empty or whitespace.</exception>
        /// <remarks>
        /// The service will create the following directory structure:
        /// - {storagePath}/metadata/ - for JSON metadata files
        /// - {storagePath}/bin/ - for binary chunk files
        /// </remarks>
        public FileStorageService(string storagePath, ILogger<FileStorageService>? logger = null)
        {
            _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            if (string.IsNullOrWhiteSpace(storagePath))
                throw new ArgumentException("Storage path cannot be empty or whitespace", nameof(storagePath));

            _metadataPath = Path.Combine(_storagePath, "metadata");
            _binPath = Path.Combine(_storagePath, "bin");
            _logger = logger;
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            // Ensure directories exist
            Directory.CreateDirectory(_metadataPath);
            Directory.CreateDirectory(_binPath);
            
            _logger?.LogInformation("FileStorageService initialized with storage path: {StoragePath}", _storagePath);
        }

        /// <summary>
        /// Retrieves metadata for all stored files.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files.</returns>
        /// <remarks>
        /// This method scans the metadata directory for all JSON files and attempts to deserialize them.
        /// Corrupted or invalid JSON files are logged and skipped.
        /// </remarks>
        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default)
        {
            List<ShelfFileMetadata> files = new List<ShelfFileMetadata>();
            
            string[] metadataFiles = Directory.GetFiles(_metadataPath, "*.json");
            _logger?.LogDebug("Found {Count} metadata files", metadataFiles.Length);
            
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
                        _logger?.LogWarning("Failed to deserialize metadata file: {File}", metadataFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error reading metadata file: {File}", metadataFile);
                }
            }
            
            return files;
        }

        /// <summary>
        /// Retrieves metadata for a specific file by its ID.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata, or <c>null</c> if the file does not exist.</returns>
        /// <remarks>
        /// This method looks for a JSON file named "{fileId}.json" in the metadata directory.
        /// Returns <c>null</c> if the file doesn't exist or cannot be deserialized.
        /// </remarks>
        public async Task<ShelfFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            string metadataFile = Path.Combine(_metadataPath, $"{fileId}.json");
            
            if (!File.Exists(metadataFile))
                return null;

            try
            {
                string jsonContent = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                return JsonSerializer.Deserialize<ShelfFileMetadata>(jsonContent, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading metadata file: {File}", metadataFile);
                return null;
            }
        }

        /// <summary>
        /// Retrieves a chunk by its ID.
        /// </summary>
        /// <param name="chunkId">The unique identifier of the chunk.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A stream containing the chunk data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified chunk does not exist.</exception>
        /// <remarks>
        /// This method looks for a binary file named "{chunkId}.bin" in the bin directory.
        /// The returned stream should be disposed when no longer needed.
        /// </remarks>
        public async Task<Stream> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken = default)
        {
            string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");
            
            if (!File.Exists(chunkFile))
                throw new FileNotFoundException($"Chunk with ID {chunkId} not found", chunkFile);

            return File.OpenRead(chunkFile);
        }

        /// <summary>
        /// Saves a chunk with the specified ID.
        /// </summary>
        /// <param name="chunkId">The unique identifier for the chunk.</param>
        /// <param name="chunkData">A stream containing the chunk data to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk ID that was saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="chunkData"/> is null.</exception>
        /// <remarks>
        /// The chunk data is written to a binary file named "{chunkId}.bin" in the bin directory.
        /// If a file with the same name already exists, it will be overwritten.
        /// The chunk data stream will be read from its current position to the end.
        /// </remarks>
        public async Task<Guid> SaveChunkAsync(Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default)
        {
            if (chunkData == null) throw new ArgumentNullException(nameof(chunkData));

            string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");
            
            using FileStream fileStream = File.Create(chunkFile);
            await chunkData.CopyToAsync(fileStream, cancellationToken);
            
            _logger?.LogDebug("Saved chunk: {ChunkId}", chunkId);
            return chunkId;
        }

        /// <summary>
        /// Saves file metadata.
        /// </summary>
        /// <param name="metadata">The file metadata to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is null.</exception>
        /// <remarks>
        /// The metadata is serialized to JSON and stored in a file named "{metadata.Id}.json" in the metadata directory.
        /// If a file with the same name already exists, it will be overwritten.
        /// </remarks>
        public async Task SaveFileMetadataAsync(ShelfFileMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            string metadataFile = Path.Combine(_metadataPath, $"{metadata.Id}.json");
            string jsonContent = JsonSerializer.Serialize(metadata, _jsonOptions);
            
            await File.WriteAllTextAsync(metadataFile, jsonContent, cancellationToken);
            
            _logger?.LogDebug("Saved metadata for file: {FileId}", metadata.Id);
        }

        /// <summary>
        /// Deletes a file and all its associated chunks.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <remarks>
        /// This method:
        /// 1. Retrieves the file metadata to get the list of chunk IDs
        /// 2. Deletes all chunk files
        /// 3. Deletes the metadata file
        /// If the file does not exist, no exception is thrown (idempotent operation).
        /// </remarks>
        public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            ShelfFileMetadata? metadata = await GetFileMetadataAsync(fileId, cancellationToken);
            
            if (metadata != null)
            {
                // Delete all chunks
                foreach (Guid chunkId in metadata.ChunkIds)
                {
                    string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");
                    if (File.Exists(chunkFile))
                    {
                        File.Delete(chunkFile);
                        _logger?.LogDebug("Deleted chunk: {ChunkId}", chunkId);
                    }
                }
                
                // Delete metadata file
                string metadataFile = Path.Combine(_metadataPath, $"{fileId}.json");
                if (File.Exists(metadataFile))
                {
                    File.Delete(metadataFile);
                    _logger?.LogDebug("Deleted metadata for file: {FileId}", fileId);
                }
            }
        }
    }
} 